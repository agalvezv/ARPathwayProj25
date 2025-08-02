using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

[Serializable]
class UuidList
{
    public List<string> uuids;
}

public class SpatialAnchorManager : MonoBehaviour
{
    public static SpatialAnchorManager Instance;

    [Header("Anchor Prefab (must already have an OVRSpatialAnchor component)")]
    [SerializeField] private GameObject _saveableAnchorPrefab;

    [Header("Spawn Transform (where new anchors appear)")]
    [SerializeField] private Transform _spawnTransform;

    private readonly List<OVRSpatialAnchor> _anchorInstances = new();
    private readonly HashSet<Guid> _anchorUuids = new();

    private readonly string _jsonFileName = "anchors.json";
    private string _jsonFilePath;

    public event Action<List<OVRSpatialAnchor>> OnAnchorsLoaded;

    public Transform ReferenceAnchorTransform =>
        _anchorInstances.Count > 0 ? _anchorInstances[0].transform : null;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else
        {
            Destroy(gameObject);
            return;
        }

        _jsonFilePath = Path.Combine(Application.persistentDataPath, _jsonFileName);
        Debug.Log($"[SpatialAnchorManager] JSON path: {_jsonFilePath}");
    }

    public void CallPoint(int sliceIndex)
    {
        switch (sliceIndex)
        {
            case 0: CreateAnchorAsync(); break;
            case 1: UnsaveAndDeleteAllAnchorsAsync(); break;
            case 2: StoreAnchorsToJson(); break;
            case 3: LoadAnchorsFromJson(); break;
            default:
                Debug.LogWarning($"[SpatialAnchorManager] No action for slice {sliceIndex}");
                break;
        }
    }

    private async void CreateAnchorAsync()
    {
        Vector3 pos = _spawnTransform.position;
        Quaternion rot = _spawnTransform.rotation;

        var go = Instantiate(_saveableAnchorPrefab, pos, rot);
        var anchor = go.GetComponent<OVRSpatialAnchor>() ?? go.AddComponent<OVRSpatialAnchor>();

        if (!await anchor.WhenLocalizedAsync())
        {
            Debug.LogError("[Create] Failed to localize anchor.");
            Destroy(go);
            return;
        }

        _anchorInstances.Add(anchor);
        Guid id = anchor.Uuid;
        _anchorUuids.Add(id);
        Debug.Log($"[Create] Localized anchor {id}");
    }

    private async void UnsaveAndDeleteAllAnchorsAsync()
    {
        ClearAnchors();
        Debug.Log("[Unsave] Cleared all anchor instances from scene.");

        HashSet<Guid> keepSet = new();
        if (File.Exists(_jsonFilePath))
        {
            string json = File.ReadAllText(_jsonFilePath);
            var wrapper = JsonUtility.FromJson<UuidList>(json);
            if (wrapper?.uuids != null && wrapper.uuids.Count > 0)
            {
                foreach (var s in wrapper.uuids)
                {
                    if (Guid.TryParse(s, out var g))
                        keepSet.Add(g);
                    else
                        Debug.LogWarning($"[Unsave] Invalid UUID in JSON: {s}");
                }
                Debug.Log($"[Unsave] Parsed {keepSet.Count} valid UUID(s) from JSON to keep.");
            }
            else
            {
                Debug.LogWarning("[Unsave] anchors.json exists but has no UUIDs; will erase all saved anchors.");
                keepSet.Clear();
            }
        }
        else
        {
            Debug.LogWarning("[Unsave] No anchors.json found; will erase all saved anchors.");
        }

        HashSet<Guid> toDelete;
        if (keepSet.Count == 0)
        {
            toDelete = new HashSet<Guid>(_anchorUuids);
        }
        else
        {
            toDelete = _anchorUuids.Where(u => !keepSet.Contains(u)).ToHashSet();
        }

        if (toDelete.Count > 0)
        {
            var eraseResult = await OVRSpatialAnchor.EraseAnchorsAsync(anchors: null, uuids: toDelete);
            if (!eraseResult.Success)
            {
                Debug.LogError($"[Unsave] Erase failed: {eraseResult.Status}");
            }
            else
            {
                Debug.Log($"[Unsave] Erased {toDelete.Count} anchor UUID(s) not present in JSON.");
            }
        }
        else
        {
            Debug.Log("[Unsave] No saved anchors needed erasing (everything matches JSON).");
        }

        if (keepSet.Count > 0)
        {
            _anchorUuids.IntersectWith(keepSet);
        }
        else
        {
            _anchorUuids.Clear();
        }

        Debug.Log($"[Unsave] Remaining saved UUIDs after prune: {_anchorUuids.Count}");
    }

    public async void StoreAnchorsToJson()
    {
        for (int i = 0; i < _anchorInstances.Count; i++)
        {
            var anchor = _anchorInstances[i];
            if (anchor == null) continue;

            Guid id = anchor.Uuid;
            if (!_anchorUuids.Contains(id))
                _anchorUuids.Add(id);

            var saveResult = await anchor.SaveAnchorAsync();
            if (saveResult.Success)
                Debug.Log($"[Store] Saved anchor {id} to cloud before writing JSON.");
            else
                Debug.LogError($"[Store] Save failed ({saveResult.Status}) for {id} before writing JSON.");
        }

        var wrapper = new UuidList { uuids = _anchorUuids.Select(g => g.ToString()).ToList() };
        string json = JsonUtility.ToJson(wrapper, prettyPrint: true);
        File.WriteAllText(_jsonFilePath, json);

        Debug.Log($"[Store] Wrote {_anchorUuids.Count} UUIDs to JSON");
        Debug.Log($"[Store] anchors.json contents:\n{File.ReadAllText(_jsonFilePath)}");

        ClearAnchors();
        Debug.Log("[Store] Cleared all current anchors from scene.");
    }

    public async void LoadAnchorsFromJson()
    {
        await Task.Delay(1000);

        if (!File.Exists(_jsonFilePath))
        {
            Debug.LogWarning("[Load] No anchors.json found.");
            return;
        }

        string json = File.ReadAllText(_jsonFilePath);
        Debug.Log($"[Load] anchors.json contents:\n{json}");

        var wrapper = JsonUtility.FromJson<UuidList>(json);
        int wrapperCount = wrapper?.uuids?.Count ?? 0;
        Debug.Log($"[Load] Parsed {wrapperCount} UUID string(s) from JSON");
        if (wrapperCount == 0)
        {
            Debug.LogWarning("[Load] JSON has no UUIDs.");
            return;
        }

        _anchorUuids.Clear();
        foreach (var s in wrapper.uuids)
        {
            if (Guid.TryParse(s, out var g))
                _anchorUuids.Add(g);
            else
                Debug.LogWarning($"[Load] Invalid UUID in JSON: {s}");
        }
        Debug.Log($"[Load] Valid GUIDs: {_anchorUuids.Count}");

        ClearAnchors();

        var unboundList = new List<OVRSpatialAnchor.UnboundAnchor>();
        var loadResult = await OVRSpatialAnchor.LoadUnboundAnchorsAsync(_anchorUuids, unboundList);
        Debug.Log($"[Load] LoadUnboundAnchorsAsync status: {loadResult.Status}, found {unboundList.Count} unbound anchors");
        if (!loadResult.Success)
            return;

        foreach (var unbound in unboundList)
        {
            Debug.Log($"[Load] Localizing {unbound.Uuid}...");
            if (!await unbound.LocalizeAsync())
            {
                Debug.LogError($"[Load] Failed to localize {unbound.Uuid}");
                continue;
            }

            var pose = unbound.Pose;
            var go = Instantiate(_saveableAnchorPrefab, pose.position, pose.rotation);
            var anchor = go.GetComponent<OVRSpatialAnchor>() ?? go.AddComponent<OVRSpatialAnchor>();
            unbound.BindTo(anchor);
            _anchorInstances.Add(anchor);
            Debug.Log($"[Load] Loaded anchor {unbound.Uuid}");
        }

        OnAnchorsLoaded?.Invoke(new List<OVRSpatialAnchor>(_anchorInstances));
    }

    public void ClearOnlyAnchorPrefabs()
    {
        foreach (var a in _anchorInstances)
            if (a != null)
                Destroy(a.gameObject);
        _anchorInstances.Clear();
    }

    public void ClearAnchors()
    {
        ClearOnlyAnchorPrefabs();

        foreach (var stray in FindObjectsOfType<OVRSpatialAnchor>())
            Destroy(stray.gameObject);
    }

    public bool HasAnchorInScene()
    {
        _anchorInstances.RemoveAll(a => a == null);
        return _anchorInstances.Count > 0;
    }
}



//using System;
//using System.Collections.Generic;
//using System.IO;
//using System.Linq;
//using System.Threading.Tasks;
//using UnityEngine;

//[Serializable]
//class UuidList
//{
//    public List<string> uuids;
//}

//public class SpatialAnchorManager : MonoBehaviour
//{
//    public static SpatialAnchorManager Instance;

//    [Header("Anchor Prefab (must already have an OVRSpatialAnchor component)")]
//    [SerializeField] private GameObject _saveableAnchorPrefab;

//    [Header("Spawn Transform (where new anchors appear)")]
//    [SerializeField] private Transform _spawnTransform;

//    private readonly List<OVRSpatialAnchor> _anchorInstances = new();
//    private readonly HashSet<Guid> _anchorUuids = new();

//    private readonly string _jsonFileName = "anchors.json";
//    private string _jsonFilePath;

//    public event Action<List<OVRSpatialAnchor>> OnAnchorsLoaded;

//    public Transform ReferenceAnchorTransform =>
//        _anchorInstances.Count > 0 ? _anchorInstances[0].transform : null;

//    private void Awake()
//    {
//        if (Instance == null) Instance = this;
//        else
//        {
//            Destroy(gameObject);
//            return;
//        }

//        _jsonFilePath = Path.Combine(Application.persistentDataPath, _jsonFileName);
//        Debug.Log($"[SpatialAnchorManager] JSON path: {_jsonFilePath}");
//    }

//    public void CallPoint(int sliceIndex)
//    {
//        switch (sliceIndex)
//        {
//            case 0: CreateAnchorAsync(); break;
//            case 1: UnsaveAndDeleteAllAnchorsAsync(); break;
//            case 2: StoreAnchorsToJson(); break;
//            case 3: LoadAnchorsFromJson(); break;
//            default:
//                Debug.LogWarning($"[SpatialAnchorManager] No action for slice {sliceIndex}");
//                break;
//        }
//    }

//    private async void CreateAnchorAsync()
//    {
//        Vector3 pos = _spawnTransform.position;
//        Quaternion rot = _spawnTransform.rotation;

//        var go = Instantiate(_saveableAnchorPrefab, pos, rot);
//        var anchor = go.GetComponent<OVRSpatialAnchor>() ?? go.AddComponent<OVRSpatialAnchor>();

//        if (!await anchor.WhenLocalizedAsync())
//        {
//            Debug.LogError("[Create] Failed to localize anchor.");
//            Destroy(go);
//            return;
//        }

//        _anchorInstances.Add(anchor);
//        Guid id = anchor.Uuid;
//        _anchorUuids.Add(id);
//        Debug.Log($"[Create] Localized anchor {id}");
//    }

//    private async void UnsaveAndDeleteAllAnchorsAsync()
//    {
//        ClearAnchors();
//        Debug.Log("[Unsave] Cleared all anchor instances from scene.");

//        HashSet<Guid> keepSet = new();
//        if (File.Exists(_jsonFilePath))
//        {
//            string json = File.ReadAllText(_jsonFilePath);
//            var wrapper = JsonUtility.FromJson<UuidList>(json);
//            if (wrapper?.uuids != null && wrapper.uuids.Count > 0)
//            {
//                foreach (var s in wrapper.uuids)
//                {
//                    if (Guid.TryParse(s, out var g))
//                        keepSet.Add(g);
//                    else
//                        Debug.LogWarning($"[Unsave] Invalid UUID in JSON: {s}");
//                }
//                Debug.Log($"[Unsave] Parsed {keepSet.Count} valid UUID(s) from JSON to keep.");
//            }
//            else
//            {
//                Debug.LogWarning("[Unsave] anchors.json exists but has no UUIDs; will erase all saved anchors.");
//                keepSet.Clear();
//            }
//        }
//        else
//        {
//            Debug.LogWarning("[Unsave] No anchors.json found; will erase all saved anchors.");
//        }

//        HashSet<Guid> toDelete;
//        if (keepSet.Count == 0)
//        {
//            toDelete = new HashSet<Guid>(_anchorUuids);
//        }
//        else
//        {
//            toDelete = _anchorUuids.Where(u => !keepSet.Contains(u)).ToHashSet();
//        }

//        if (toDelete.Count > 0)
//        {
//            var eraseResult = await OVRSpatialAnchor.EraseAnchorsAsync(anchors: null, uuids: toDelete);
//            if (!eraseResult.Success)
//            {
//                Debug.LogError($"[Unsave] Erase failed: {eraseResult.Status}");
//            }
//            else
//            {
//                Debug.Log($"[Unsave] Erased {toDelete.Count} anchor UUID(s) not present in JSON.");
//            }
//        }
//        else
//        {
//            Debug.Log("[Unsave] No saved anchors needed erasing (everything matches JSON).");
//        }

//        if (keepSet.Count > 0)
//        {
//            _anchorUuids.IntersectWith(keepSet);
//        }
//        else
//        {
//            _anchorUuids.Clear();
//        }

//        Debug.Log($"[Unsave] Remaining saved UUIDs after prune: {_anchorUuids.Count}");
//    }

//    public async void StoreAnchorsToJson()
//    {
//        for (int i = 0; i < _anchorInstances.Count; i++)
//        {
//            var anchor = _anchorInstances[i];
//            if (anchor == null) continue;

//            Guid id = anchor.Uuid;
//            if (!_anchorUuids.Contains(id))
//                _anchorUuids.Add(id);

//            var saveResult = await anchor.SaveAnchorAsync();
//            if (saveResult.Success)
//                Debug.Log($"[Store] Saved anchor {id} to cloud before writing JSON.");
//            else
//                Debug.LogError($"[Store] Save failed ({saveResult.Status}) for {id} before writing JSON.");
//        }

//        var wrapper = new UuidList { uuids = _anchorUuids.Select(g => g.ToString()).ToList() };
//        string json = JsonUtility.ToJson(wrapper, prettyPrint: true);
//        File.WriteAllText(_jsonFilePath, json);

//        Debug.Log($"[Store] Wrote {_anchorUuids.Count} UUIDs to JSON");
//        Debug.Log($"[Store] anchors.json contents:\n{File.ReadAllText(_jsonFilePath)}");

//        ClearAnchors();
//        Debug.Log("[Store] Cleared all current anchors from scene.");
//    }

//    public async void LoadAnchorsFromJson()
//    {
//        await Task.Delay(1000);

//        if (!File.Exists(_jsonFilePath))
//        {
//            Debug.LogWarning("[Load] No anchors.json found.");
//            return;
//        }

//        string json = File.ReadAllText(_jsonFilePath);
//        Debug.Log($"[Load] anchors.json contents:\n{json}");

//        var wrapper = JsonUtility.FromJson<UuidList>(json);
//        int wrapperCount = wrapper?.uuids?.Count ?? 0;
//        Debug.Log($"[Load] Parsed {wrapperCount} UUID string(s) from JSON");
//        if (wrapperCount == 0)
//        {
//            Debug.LogWarning("[Load] JSON has no UUIDs.");
//            return;
//        }

//        _anchorUuids.Clear();
//        foreach (var s in wrapper.uuids)
//        {
//            if (Guid.TryParse(s, out var g))
//                _anchorUuids.Add(g);
//            else
//                Debug.LogWarning($"[Load] Invalid UUID in JSON: {s}");
//        }
//        Debug.Log($"[Load] Valid GUIDs: {_anchorUuids.Count}");

//        ClearAnchors();

//        var unboundList = new List<OVRSpatialAnchor.UnboundAnchor>();
//        var loadResult = await OVRSpatialAnchor.LoadUnboundAnchorsAsync(_anchorUuids, unboundList);
//        Debug.Log($"[Load] LoadUnboundAnchorsAsync status: {loadResult.Status}, found {unboundList.Count} unbound anchors");
//        if (!loadResult.Success)
//            return;

//        foreach (var unbound in unboundList)
//        {
//            Debug.Log($"[Load] Localizing {unbound.Uuid}...");
//            if (!await unbound.LocalizeAsync())
//            {
//                Debug.LogError($"[Load] Failed to localize {unbound.Uuid}");
//                continue;
//            }

//            var pose = unbound.Pose;
//            var go = Instantiate(_saveableAnchorPrefab, pose.position, pose.rotation);
//            var anchor = go.GetComponent<OVRSpatialAnchor>() ?? go.AddComponent<OVRSpatialAnchor>();
//            unbound.BindTo(anchor);
//            _anchorInstances.Add(anchor);
//            Debug.Log($"[Load] Loaded anchor {unbound.Uuid}");
//        }

//        OnAnchorsLoaded?.Invoke(new List<OVRSpatialAnchor>(_anchorInstances));
//    }

//    public void ClearAnchors()
//    {
//        foreach (var a in _anchorInstances)
//            if (a != null)
//                Destroy(a.gameObject);
//        _anchorInstances.Clear();

//        foreach (var stray in FindObjectsOfType<OVRSpatialAnchor>())
//            Destroy(stray.gameObject);
//    }
//}




//using System;
//using System.Collections.Generic;
//using System.IO;
//using System.Linq;
//using System.Threading.Tasks;
//using UnityEngine;

//[Serializable]
//class UuidList
//{
//    public List<string> uuids;
//}

//public class SpatialAnchorManager : MonoBehaviour
//{
//    public static SpatialAnchorManager Instance;

//    [Header("Anchor Prefab (must already have an OVRSpatialAnchor component)")]
//    [SerializeField] private GameObject _saveableAnchorPrefab;

//    [Header("Spawn Transform (where new anchors appear)")]
//    [SerializeField] private Transform _spawnTransform;

//    private readonly List<OVRSpatialAnchor> _anchorInstances = new();
//    private readonly HashSet<Guid> _anchorUuids = new();

//    private readonly string _jsonFileName = "anchors.json";
//    private string _jsonFilePath;

//    public event Action<List<OVRSpatialAnchor>> OnAnchorsLoaded;

//    public Transform ReferenceAnchorTransform =>
//        _anchorInstances.Count > 0 ? _anchorInstances[0].transform : null;

//    private void Awake()
//    {
//        if (Instance == null) Instance = this;
//        else
//        {
//            Destroy(gameObject);
//            return;
//        }

//        _jsonFilePath = Path.Combine(Application.persistentDataPath, _jsonFileName);
//        Debug.Log($"[SpatialAnchorManager] JSON path: {_jsonFilePath}");
//    }

//    public void CallPoint(int sliceIndex)
//    {
//        switch (sliceIndex)
//        {
//            case 0: CreateAndSaveAnchorAsync(); break;
//            case 1: ClearSceneAnchors(); break;
//            case 2: TransferToJSONFunctions(); break; // Replaces StoreAnchorsToJson
//            case 3: LoadAnchorsFromJson(); break;
//            default:
//                Debug.LogWarning($"[SpatialAnchorManager] No action for slice {sliceIndex}");
//                break;
//        }
//    }

//    private async void CreateAndSaveAnchorAsync()
//    {
//        Vector3 pos = _spawnTransform.position;
//        Quaternion rot = _spawnTransform.rotation;

//        var go = Instantiate(_saveableAnchorPrefab, pos, rot);
//        var anchor = go.GetComponent<OVRSpatialAnchor>() ?? go.AddComponent<OVRSpatialAnchor>();

//        if (!await anchor.WhenLocalizedAsync())
//        {
//            Debug.LogError("[Create] Failed to localize anchor.");
//            Destroy(go);
//            return;
//        }

//        _anchorInstances.Add(anchor);
//        Debug.Log($"[Create] Created and localized anchor (not saved yet) {anchor.Uuid}");
//    }

//    public async void TransferToJSONFunctions()
//    {
//        Debug.Log("[Transfer] Starting anchor transfer to JSON");

//        // Step 1: Erase all previously saved anchors
//        var eraseResult = await OVRSpatialAnchor.EraseAnchorsAsync(anchors: null, uuids: null);
//        if (!eraseResult.Success)
//        {
//            Debug.LogError($"[Transfer] Failed to erase saved anchors: {eraseResult.Status}");
//            return;
//        }
//        Debug.Log("[Transfer] Successfully erased all saved anchors from persistent storage");

//        // Step 2: Save current in-scene anchors
//        var wrapper = new UuidList();
//        wrapper.uuids = new List<string>();
//        _anchorUuids.Clear();

//        foreach (var anchor in _anchorInstances)
//        {
//            if (anchor == null)
//                continue;

//            var saveResult = await anchor.SaveAnchorAsync();
//            if (saveResult.Success)
//            {
//                _anchorUuids.Add(anchor.Uuid);
//                wrapper.uuids.Add(anchor.Uuid.ToString());
//                Debug.Log($"[Transfer] Saved anchor {anchor.Uuid}");
//            }
//            else
//            {
//                Debug.LogError($"[Transfer] Failed to save anchor {anchor.Uuid} ({saveResult.Status})");
//            }
//        }

//        // Step 3: Write to JSON
//        string json = JsonUtility.ToJson(wrapper, prettyPrint: true);
//        File.WriteAllText(_jsonFilePath, json);

//        Debug.Log($"[Transfer] Stored {_anchorUuids.Count} anchors to JSON");
//        Debug.Log($"[Transfer] anchors.json contents:\n{json}");

//        ClearAnchors();
//        Debug.Log("[Transfer] Cleared all anchors from the scene after transfer");
//    }

//    //public async void LoadAnchorsFromJson()
//    //{
//    //    // give a moment for environment tracking to warm up / stabilize
//    //    await Task.Delay(1000);

//    //    if (!File.Exists(_jsonFilePath))
//    //    {
//    //        Debug.LogWarning("[Load] No anchors.json found.");
//    //        return;
//    //    }

//    //    string json = File.ReadAllText(_jsonFilePath);
//    //    Debug.Log($"[Load] anchors.json contents:\n{json}");

//    //    var wrapper = JsonUtility.FromJson<UuidList>(json);
//    //    int wrapperCount = wrapper?.uuids?.Count ?? 0;
//    //    Debug.Log($"[Load] Parsed {wrapperCount} UUID string(s) from JSON");
//    //    if (wrapperCount == 0)
//    //    {
//    //        Debug.LogWarning("[Load] JSON has no UUIDs.");
//    //        return;
//    //    }

//    //    _anchorUuids.Clear();
//    //    foreach (var s in wrapper.uuids)
//    //    {
//    //        if (Guid.TryParse(s, out var g))
//    //            _anchorUuids.Add(g);
//    //        else
//    //            Debug.LogWarning($"[Load] Invalid UUID in JSON: {s}");
//    //    }
//    //    Debug.Log($"[Load] Valid GUIDs: {_anchorUuids.Count}");
//    //    if (_anchorUuids.Count == 0)
//    //    {
//    //        Debug.LogWarning("[Load] No valid GUIDs to load.");
//    //        return;
//    //    }

//    //    ClearAnchors();

//    //    var unboundList = new List<OVRSpatialAnchor.UnboundAnchor>();
//    //    var loadResult = await OVRSpatialAnchor.LoadUnboundAnchorsAsync(_anchorUuids, unboundList);
//    //    Debug.Log($"[Load] LoadUnboundAnchorsAsync status: {loadResult.Status}, found {unboundList.Count} unbound anchors");
//    //    if (!loadResult.Success || unboundList.Count == 0)
//    //    {
//    //        Debug.LogError("[Load] Failed to retrieve unbound anchors or none returned.");
//    //        return;
//    //    }

//    //    foreach (var unbound in unboundList)
//    //    {
//    //        // Retry localization with backoff
//    //        bool localized = false;
//    //        const int maxAttempts = 5;
//    //        for (int attempt = 1; attempt <= maxAttempts; attempt++)
//    //        {
//    //            localized = await unbound.LocalizeAsync();
//    //            if (localized)
//    //                break;

//    //            Debug.Log($"[Load] Localization attempt {attempt} failed for {unbound.Uuid}, retrying after delay...");
//    //            await Task.Delay(1000 * attempt);
//    //        }

//    //        if (!localized)
//    //        {
//    //            Debug.LogError($"[Load] Failed to localize anchor {unbound.Uuid} after {maxAttempts} attempts.");
//    //            continue;
//    //        }

//    //        // Instantiate and bind (do NOT manually set pose)
//    //        var go = Instantiate(_saveableAnchorPrefab);
//    //        var anchor = go.GetComponent<OVRSpatialAnchor>() ?? go.AddComponent<OVRSpatialAnchor>();
//    //        unbound.BindTo(anchor);

//    //        // Wait for the bound anchor to stabilize if possible
//    //        if (!await anchor.WhenLocalizedAsync())
//    //        {
//    //            Debug.LogWarning($"[Load] Bound anchor {unbound.Uuid} is not fully localized (continuing anyway).");
//    //        }

//    //        _anchorInstances.Add(anchor);
//    //        Debug.Log($"[Load] Successfully loaded and bound anchor {unbound.Uuid}");
//    //    }

//    //    OnAnchorsLoaded?.Invoke(new List<OVRSpatialAnchor>(_anchorInstances));
//    //}

//    public async void LoadAnchorsFromJson()
//    {
//        // give a moment for environment tracking to warm up / stabilize
//        await Task.Delay(1000);

//        if (!File.Exists(_jsonFilePath))
//        {
//            Debug.LogWarning("[Load] No anchors.json found.");
//            return;
//        }

//        string json = File.ReadAllText(_jsonFilePath);
//        Debug.Log($"[Load] anchors.json contents:\n{json}");

//        var wrapper = JsonUtility.FromJson<UuidList>(json);
//        int wrapperCount = wrapper?.uuids?.Count ?? 0;
//        Debug.Log($"[Load] Parsed {wrapperCount} UUID string(s) from JSON");
//        if (wrapperCount == 0)
//        {
//            Debug.LogWarning("[Load] JSON has no UUIDs.");
//            return;
//        }

//        _anchorUuids.Clear();
//        foreach (var s in wrapper.uuids)
//        {
//            if (Guid.TryParse(s, out var g))
//                _anchorUuids.Add(g);
//            else
//                Debug.LogWarning($"[Load] Invalid UUID in JSON: {s}");
//        }
//        Debug.Log($"[Load] Valid GUIDs: {_anchorUuids.Count}");
//        if (_anchorUuids.Count == 0)
//        {
//            Debug.LogWarning("[Load] No valid GUIDs to load.");
//            return;
//        }

//        ClearAnchors();

//        var unboundList = new List<OVRSpatialAnchor.UnboundAnchor>();
//        var loadResult = await OVRSpatialAnchor.LoadUnboundAnchorsAsync(_anchorUuids, unboundList);
//        Debug.Log($"[Load] LoadUnboundAnchorsAsync status: {loadResult.Status}, found {unboundList.Count} unbound anchors");
//        if (!loadResult.Success || unboundList.Count == 0)
//        {
//            Debug.LogError("[Load] Failed to retrieve unbound anchors or none returned.");
//            return;
//        }

//        foreach (var unbound in unboundList)
//        {
//            // Ensure the unbound anchor is localized (with backoff/retries)
//            bool localized = false;
//            if (unbound.Localized)
//            {
//                localized = true;
//                Debug.Log($"[Load] Unbound anchor {unbound.Uuid} was already localized.");
//            }
//            else if (unbound.Localizing)
//            {
//                const int maxWaitMs = 5000;
//                int waited = 0;
//                while (unbound.Localizing && waited < maxWaitMs)
//                {
//                    await Task.Delay(200);
//                    waited += 200;
//                }
//                if (unbound.Localized)
//                {
//                    localized = true;
//                    Debug.Log($"[Load] Unbound anchor {unbound.Uuid} finished localizing after waiting.");
//                }
//                else
//                {
//                    Debug.Log($"[Load] Unbound anchor {unbound.Uuid} still not localized; will try explicit LocalizeAsync.");
//                }
//            }

//            if (!localized)
//            {
//                const int maxAttempts = 5;
//                for (int attempt = 1; attempt <= maxAttempts; attempt++)
//                {
//                    localized = await unbound.LocalizeAsync();
//                    if (localized)
//                        break;
//                    Debug.Log($"[Load] Localization attempt {attempt} failed for {unbound.Uuid}, retrying after delay...");
//                    await Task.Delay(1000 * attempt);
//                }
//            }

//            if (!localized)
//            {
//                Debug.LogError($"[Load] Failed to localize anchor {unbound.Uuid} after retries.");
//                continue;
//            }

//            // Capture expected pose from unbound (for diagnostics)
//            Pose expectedPose = unbound.Pose;
//            Debug.Log($"[Load] Unbound anchor {unbound.Uuid} expected pose position={expectedPose.position}, rotation={expectedPose.rotation.eulerAngles}");

//            // Hybrid: instantiate at expected pose (old behavior) then bind
//            var go = Instantiate(_saveableAnchorPrefab, expectedPose.position, expectedPose.rotation);
//            var anchor = go.GetComponent<OVRSpatialAnchor>() ?? go.AddComponent<OVRSpatialAnchor>();
//            unbound.BindTo(anchor);

//            // Wait for the bound anchor to settle
//            bool boundLocalized = await anchor.WhenLocalizedAsync();
//            if (!boundLocalized)
//            {
//                Debug.LogWarning($"[Load] Bound anchor {unbound.Uuid} did not fully report localized; continuing anyway.");
//            }

//            // Diagnostics: compare expected vs actual
//            Vector3 actualPos = anchor.transform.position;
//            Quaternion actualRot = anchor.transform.rotation;

//            float posDelta = Vector3.Distance(expectedPose.position, actualPos);
//            float angleDelta = Quaternion.Angle(expectedPose.rotation, actualRot);

//            Debug.Log($"[Load] Anchor {unbound.Uuid} recovered transform position={actualPos}, rotation={actualRot.eulerAngles}");
//            Debug.Log($"[Load] Delta for {unbound.Uuid}: position {posDelta:F3}m, rotation {angleDelta:F2}°");

//            if (posDelta > 0.1f || angleDelta > 5f)
//            {
//                Debug.LogWarning($"[Load] Anchor {unbound.Uuid} deviates beyond tolerance. Possible localization instability or world origin drift.");
//            }

//            _anchorInstances.Add(anchor);
//            Debug.Log($"[Load] Successfully loaded and bound anchor {unbound.Uuid}");
//        }

//        OnAnchorsLoaded?.Invoke(new List<OVRSpatialAnchor>(_anchorInstances));
//    }



//    public void ClearAnchors()
//    {
//        foreach (var a in _anchorInstances)
//            if (a != null)
//                Destroy(a.gameObject);
//        _anchorInstances.Clear();

//        foreach (var stray in FindObjectsOfType<OVRSpatialAnchor>())
//            Destroy(stray.gameObject);
//    }

//    public void ClearSceneAnchors()
//    {
//        ClearAnchors();
//        Debug.Log("[ClearScene] Removed all anchors from the current scene only. Persistent storage untouched.");
//    }

//    public bool HasAnchorsInScene() => _anchorInstances.Count > 0;
//}











//using System;
//using System.Collections.Generic;
//using System.IO;
//using System.Linq;
//using System.Threading.Tasks;
//using UnityEngine;

//[Serializable]
//class UuidList
//{
//    public List<string> uuids;
//}

//public class SpatialAnchorManager : MonoBehaviour
//{
//    public static SpatialAnchorManager Instance;

//    [Header("Anchor Prefab (must already have an OVRSpatialAnchor component)")]
//    [SerializeField] private GameObject _saveableAnchorPrefab;

//    [Header("Spawn Transform (where new anchors appear)")]
//    [SerializeField] private Transform _spawnTransform;

//    private readonly List<OVRSpatialAnchor> _anchorInstances = new();
//    private readonly HashSet<Guid> _anchorUuids = new();

//    private readonly string _jsonFileName = "anchors.json";
//    private string _jsonFilePath;

//    public event Action<List<OVRSpatialAnchor>> OnAnchorsLoaded;

//    public Transform ReferenceAnchorTransform =>
//        _anchorInstances.Count > 0 ? _anchorInstances[0].transform : null;

//    private void Awake()
//    {
//        if (Instance == null) Instance = this;
//        else
//        {
//            Destroy(gameObject);
//            return;
//        }

//        _jsonFilePath = Path.Combine(Application.persistentDataPath, _jsonFileName);
//        Debug.Log($"[SpatialAnchorManager] JSON path: {_jsonFilePath}");
//    }

//    public void CallPoint(int sliceIndex)
//    {
//        switch (sliceIndex)
//        {
//            case 0: CreateAndSaveAnchorAsync(); break;
//            case 1: ClearSceneAnchors(); break;
//            case 2: TransferToJSONFunctions(); break; // Replaces StoreAnchorsToJson
//            case 3: LoadAnchorsFromJson(); break;
//            default:
//                Debug.LogWarning($"[SpatialAnchorManager] No action for slice {sliceIndex}");
//                break;
//        }
//    }

//    private async void CreateAndSaveAnchorAsync()
//    {
//        Vector3 pos = _spawnTransform.position;
//        Quaternion rot = _spawnTransform.rotation;

//        var go = Instantiate(_saveableAnchorPrefab, pos, rot);
//        var anchor = go.GetComponent<OVRSpatialAnchor>() ?? go.AddComponent<OVRSpatialAnchor>();

//        if (!await anchor.WhenLocalizedAsync())
//        {
//            Debug.LogError("[Create] Failed to localize anchor.");
//            Destroy(go);
//            return;
//        }

//        _anchorInstances.Add(anchor);
//        Debug.Log($"[Create] Created and localized anchor (not saved yet) {anchor.Uuid}");
//    }

//    public async void TransferToJSONFunctions()
//    {
//        Debug.Log("[Transfer] Starting anchor transfer to JSON");

//        // Step 1: Erase all previously saved anchors
//        var eraseResult = await OVRSpatialAnchor.EraseAnchorsAsync(anchors: null, uuids: null);
//        if (!eraseResult.Success)
//        {
//            Debug.LogError($"[Transfer] Failed to erase saved anchors: {eraseResult.Status}");
//            return;
//        }
//        Debug.Log("[Transfer] Successfully erased all saved anchors from persistent storage");

//        // Step 2: Save current in-scene anchors
//        var wrapper = new UuidList();
//        wrapper.uuids = new List<string>();
//        _anchorUuids.Clear();

//        foreach (var anchor in _anchorInstances)
//        {
//            if (anchor == null)
//                continue;

//            var saveResult = await anchor.SaveAnchorAsync();
//            if (saveResult.Success)
//            {
//                _anchorUuids.Add(anchor.Uuid);
//                wrapper.uuids.Add(anchor.Uuid.ToString());
//                Debug.Log($"[Transfer] Saved anchor {anchor.Uuid}");
//            }
//            else
//            {
//                Debug.LogError($"[Transfer] Failed to save anchor {anchor.Uuid} ({saveResult.Status})");
//            }
//        }

//        // Step 3: Write to JSON
//        string json = JsonUtility.ToJson(wrapper, prettyPrint: true);
//        File.WriteAllText(_jsonFilePath, json);

//        Debug.Log($"[Transfer] Stored {_anchorUuids.Count} anchors to JSON");
//        Debug.Log($"[Transfer] anchors.json contents:\n{json}");

//        ClearAnchors();
//        Debug.Log("[Transfer] Cleared all anchors from the scene after transfer");
//    }

//    public async void LoadAnchorsFromJson()
//    {
//        await Task.Delay(1000);

//        if (!File.Exists(_jsonFilePath))
//        {
//            Debug.LogWarning("[Load] No anchors.json found.");
//            return;
//        }

//        string json = File.ReadAllText(_jsonFilePath);
//        Debug.Log($"[Load] anchors.json contents:\n{json}");

//        var wrapper = JsonUtility.FromJson<UuidList>(json);
//        int wrapperCount = wrapper?.uuids?.Count ?? 0;
//        Debug.Log($"[Load] Parsed {wrapperCount} UUID string(s) from JSON");
//        if (wrapperCount == 0)
//        {
//            Debug.LogWarning("[Load] JSON has no UUIDs.");
//            return;
//        }

//        _anchorUuids.Clear();
//        foreach (var s in wrapper.uuids)
//        {
//            if (Guid.TryParse(s, out var g))
//                _anchorUuids.Add(g);
//            else
//                Debug.LogWarning($"[Load] Invalid UUID in JSON: {s}");
//        }
//        Debug.Log($"[Load] Valid GUIDs: {_anchorUuids.Count}");

//        ClearAnchors();

//        var unboundList = new List<OVRSpatialAnchor.UnboundAnchor>();
//        var loadResult = await OVRSpatialAnchor.LoadUnboundAnchorsAsync(_anchorUuids, unboundList);
//        Debug.Log($"[Load] LoadUnboundAnchorsAsync status: {loadResult.Status}, found {unboundList.Count} unbound anchors");
//        if (!loadResult.Success)
//            return;

//        foreach (var unbound in unboundList)
//        {
//            Debug.Log($"[Load] Localizing {unbound.Uuid}...");
//            if (!await unbound.LocalizeAsync())
//            {
//                Debug.LogError($"[Load] Failed to localize {unbound.Uuid}");
//                continue;
//            }

//            var pose = unbound.Pose;
//            var go = Instantiate(_saveableAnchorPrefab, pose.position, pose.rotation);
//            var anchor = go.GetComponent<OVRSpatialAnchor>() ?? go.AddComponent<OVRSpatialAnchor>();
//            unbound.BindTo(anchor);
//            _anchorInstances.Add(anchor);
//            Debug.Log($"[Load] Loaded anchor {unbound.Uuid}");
//        }

//        OnAnchorsLoaded?.Invoke(new List<OVRSpatialAnchor>(_anchorInstances));
//    }

//    public void ClearAnchors()
//    {
//        foreach (var a in _anchorInstances)
//            if (a != null)
//                Destroy(a.gameObject);
//        _anchorInstances.Clear();

//        foreach (var stray in FindObjectsOfType<OVRSpatialAnchor>())
//            Destroy(stray.gameObject);
//    }

//    public void ClearSceneAnchors()
//    {
//        ClearAnchors();
//        Debug.Log("[ClearScene] Removed all anchors from the current scene only. Persistent storage untouched.");
//    }

//    public bool HasAnchorsInScene() => _anchorInstances.Count > 0;

//}




//using System;
//using System.Collections.Generic;
//using System.IO;
//using System.Linq;
//using System.Threading.Tasks;     
//using UnityEngine;


//[Serializable]
//class UuidList
//{
//    public List<string> uuids;
//}

//public class SpatialAnchorManager : MonoBehaviour
//{
//    public static SpatialAnchorManager Instance;

//    [Header("Anchor Prefab (must already have an OVRSpatialAnchor component)")]
//    [SerializeField] private GameObject _saveableAnchorPrefab;

//    [Header("Spawn Transform (where new anchors appear)")]
//    [SerializeField] private Transform _spawnTransform;


//    private readonly List<OVRSpatialAnchor> _anchorInstances = new();


//    private readonly HashSet<Guid> _anchorUuids = new();


//    private readonly string _jsonFileName = "anchors.json";
//    private string _jsonFilePath;

//    public event Action<List<OVRSpatialAnchor>> OnAnchorsLoaded;

//    public Transform ReferenceAnchorTransform =>
//        _anchorInstances.Count > 0 ? _anchorInstances[0].transform : null;

//    private void Awake()
//    {
//        if (Instance == null) Instance = this;
//        else
//        {
//            Destroy(gameObject);
//            return;
//        }

//        _jsonFilePath = Path.Combine(Application.persistentDataPath, _jsonFileName);
//        Debug.Log($"[SpatialAnchorManager] JSON path: {_jsonFilePath}");
//    }

//    public void CallPoint(int sliceIndex)
//    {
//        switch (sliceIndex)
//        {
//            case 0: CreateAndSaveAnchorAsync(); break;
//            case 1: UnsaveAndDeleteAllAnchorsAsync(); break;
//            case 2: StoreAnchorsToJson(); break;
//            case 3: LoadAnchorsFromJson(); break;
//            default:
//                Debug.LogWarning($"[SpatialAnchorManager] No action for slice {sliceIndex}");
//                break;
//        }
//    }

//    private async void CreateAndSaveAnchorAsync()
//    {
//        Vector3 pos = _spawnTransform.position;
//        Quaternion rot = _spawnTransform.rotation;

//        var go = Instantiate(_saveableAnchorPrefab, pos, rot);
//        var anchor = go.GetComponent<OVRSpatialAnchor>() ?? go.AddComponent<OVRSpatialAnchor>();

//        if (!await anchor.WhenLocalizedAsync())
//        {
//            Debug.LogError("[Create] Failed to localize anchor.");
//            Destroy(go);
//            return;
//        }

//        _anchorInstances.Add(anchor);
//        Guid id = anchor.Uuid;
//        _anchorUuids.Add(id);
//        Debug.Log($"[Create] Localized anchor {id}");

//        var saveResult = await anchor.SaveAnchorAsync();
//        if (saveResult.Success)
//            Debug.Log($"[Create] Saved anchor {id}");
//        else
//            Debug.LogError($"[Create] Save failed ({saveResult.Status}) for {id}");
//    }

//    private async void UnsaveAndDeleteAllAnchorsAsync()
//    {
//        var eraseResult = await OVRSpatialAnchor.EraseAnchorsAsync(anchors: null, uuids: _anchorUuids);
//        if (!eraseResult.Success)
//        {
//            Debug.LogError($"[Unsave] Erase failed: {eraseResult.Status}");
//            return;
//        }

//        _anchorUuids.Clear();
//        Debug.Log("[Unsave] Cleared saved UUIDs from storage.");

//        ClearAnchors();
//        Debug.Log("[Unsave] Destroyed all anchor instances.");
//    }

//    public void StoreAnchorsToJson()
//    {
//        var wrapper = new UuidList { uuids = _anchorUuids.Select(g => g.ToString()).ToList() };
//        string json = JsonUtility.ToJson(wrapper, prettyPrint: true);
//        File.WriteAllText(_jsonFilePath, json);

//        Debug.Log($"[Store] Wrote {_anchorUuids.Count} UUIDs to JSON");
//        Debug.Log($"[Store] anchors.json contents:\n{File.ReadAllText(_jsonFilePath)}");  

//        ClearAnchors();
//        Debug.Log("[Store] Cleared all current anchors from scene.");
//    }

//    public async void LoadAnchorsFromJson()
//    {

//        await Task.Delay(1000);

//        if (!File.Exists(_jsonFilePath))
//        {
//            Debug.LogWarning("[Load] No anchors.json found.");
//            return;
//        }


//        string json = File.ReadAllText(_jsonFilePath);
//        Debug.Log($"[Load] anchors.json contents:\n{json}");


//        var wrapper = JsonUtility.FromJson<UuidList>(json);
//        int wrapperCount = wrapper?.uuids?.Count ?? 0;
//        Debug.Log($"[Load] Parsed {wrapperCount} UUID string(s) from JSON");
//        if (wrapperCount == 0)
//        {
//            Debug.LogWarning("[Load] JSON has no UUIDs.");
//            return;
//        }

//        _anchorUuids.Clear();
//        foreach (var s in wrapper.uuids)
//        {
//            if (Guid.TryParse(s, out var g))
//                _anchorUuids.Add(g);
//            else
//                Debug.LogWarning($"[Load] Invalid UUID in JSON: {s}");
//        }
//        Debug.Log($"[Load] Valid GUIDs: {_anchorUuids.Count}");

//        ClearAnchors();


//        var unboundList = new List<OVRSpatialAnchor.UnboundAnchor>();
//        var loadResult = await OVRSpatialAnchor.LoadUnboundAnchorsAsync(_anchorUuids, unboundList);
//        Debug.Log($"[Load] LoadUnboundAnchorsAsync status: {loadResult.Status}, found {unboundList.Count} unbound anchors");
//        if (!loadResult.Success)
//            return;


//        foreach (var unbound in unboundList)
//        {
//            Debug.Log($"[Load] Localizing {unbound.Uuid}...");
//            if (!await unbound.LocalizeAsync())
//            {
//                Debug.LogError($"[Load] Failed to localize {unbound.Uuid}");
//                continue;
//            }

//            var pose = unbound.Pose;
//            var go = Instantiate(_saveableAnchorPrefab, pose.position, pose.rotation);
//            var anchor = go.GetComponent<OVRSpatialAnchor>() ?? go.AddComponent<OVRSpatialAnchor>();
//            unbound.BindTo(anchor);
//            _anchorInstances.Add(anchor);
//            Debug.Log($"[Load] Loaded anchor {unbound.Uuid}");
//        }


//        OnAnchorsLoaded?.Invoke(new List<OVRSpatialAnchor>(_anchorInstances));
//    }


//    public void ClearAnchors()
//    {
//        foreach (var a in _anchorInstances)
//            if (a != null)
//                Destroy(a.gameObject);
//        _anchorInstances.Clear();

//        foreach (var stray in FindObjectsOfType<OVRSpatialAnchor>())
//            Destroy(stray.gameObject);
//    }
//}
