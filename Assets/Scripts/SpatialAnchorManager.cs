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
            case 0: CreateAndSaveAnchorAsync(); break;
            case 1: UnsaveAndDeleteAllAnchorsAsync(); break;
            case 2: StoreAnchorsToJson(); break;
            case 3: LoadAnchorsFromJson(); break;
            default:
                Debug.LogWarning($"[SpatialAnchorManager] No action for slice {sliceIndex}");
                break;
        }
    }

    private async void CreateAndSaveAnchorAsync()
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

        var saveResult = await anchor.SaveAnchorAsync();
        if (saveResult.Success)
            Debug.Log($"[Create] Saved anchor {id}");
        else
            Debug.LogError($"[Create] Save failed ({saveResult.Status}) for {id}");
    }

    private async void UnsaveAndDeleteAllAnchorsAsync()
    {
        var eraseResult = await OVRSpatialAnchor.EraseAnchorsAsync(anchors: null, uuids: _anchorUuids);
        if (!eraseResult.Success)
        {
            Debug.LogError($"[Unsave] Erase failed: {eraseResult.Status}");
            return;
        }

        _anchorUuids.Clear();
        Debug.Log("[Unsave] Cleared saved UUIDs from storage.");

        ClearAnchors();
        Debug.Log("[Unsave] Destroyed all anchor instances.");
    }

    public void StoreAnchorsToJson()
    {
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


    public void ClearAnchors()
    {
        foreach (var a in _anchorInstances)
            if (a != null)
                Destroy(a.gameObject);
        _anchorInstances.Clear();

        foreach (var stray in FindObjectsOfType<OVRSpatialAnchor>())
            Destroy(stray.gameObject);
    }
}
