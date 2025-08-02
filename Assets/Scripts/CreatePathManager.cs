using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class CreatePathManager : MonoBehaviour
{
    [Header("Prefabs & Anchors")]
    public GameObject navPointPrefab;
    public Transform rightHandAnchor;

    [Header("UI Elements")]
    public GameObject handMenu;
    public TMP_Dropdown pathDropdown;
    public Button loadButton;

    [Header("QR Scanner")]
    public QRPhotoScanner qrScanner;

    private List<GameObject> navPoints = new List<GameObject>();
    private GameObject latestNavPoint;

    public string string_start = " ";
    public bool game_start = false;

    void Start()
    {
        if (loadButton != null)
            loadButton.onClick.AddListener(OnLoadButtonClicked);
    }

    public void CallPoint(int sliceIndex)
    {
        switch (sliceIndex)
        {
            case 0: CreateNavPoint(); break;
            case 1: DeleteNavPoint(); break;
            case 2: DeleteAllNavPoints(); break;
            case 3: SavePath(); break;
            case 4: LoadPathMenu(); break;
            case 5: ClearPathsMenu(); break;
            case 6: StartGame(); break;
            default:
                Debug.LogWarning($"[CreatePathManager] No action for slice {sliceIndex}");
                break;
        }
    }

    public void CreateNavPoint()
    {
        if (navPointPrefab == null)
        {
            Debug.LogError("NavPoint prefab is not assigned.");
            return;
        }
        Vector3 pos = rightHandAnchor != null ? rightHandAnchor.position : transform.position;
        Quaternion rot = rightHandAnchor != null ? rightHandAnchor.rotation : transform.rotation;
        var go = Instantiate(navPointPrefab, pos, rot);
        navPoints.Add(go);
        latestNavPoint = go;
    }

    public void DeleteNavPoint()
    {
        if (navPoints.Count == 0)
        {
            Debug.LogWarning("No navigation points to delete.");
            latestNavPoint = null;
            return;
        }
        int i = navPoints.Count - 1;
        Destroy(navPoints[i]);
        navPoints.RemoveAt(i);
        latestNavPoint = navPoints.Count > 0 ? navPoints[^1] : null;
    }

    public void DeleteAllNavPoints()
    {
        for (int i = navPoints.Count - 1; i >= 0; i--)
            Destroy(navPoints[i]);
        navPoints.Clear();
        latestNavPoint = null;
    }

    public void SavePath()
    {
        if (navPoints.Count == 0)
        {
            Debug.LogWarning("No navigation points to save.");
            return;
        }
        StartCoroutine(SavePathCoroutine());
    }

    private IEnumerator SavePathCoroutine()
    {
        foreach (var anchor in FindObjectsOfType<OVRSpatialAnchor>())
            Destroy(anchor.gameObject);
        Debug.Log("[SavePath] Cleared existing anchors from scene.");

        SpatialAnchorManager.Instance.LoadAnchorsFromJson();
        Debug.Log("[SavePath] Loading anchors from anchors.json...");

        float timeout = 5f, timer = 0f;
        while (FindObjectsOfType<OVRSpatialAnchor>().Length == 0 && timer < timeout)
        {
            timer += Time.deltaTime;
            yield return null;
        }

        var anchors = FindObjectsOfType<OVRSpatialAnchor>();
        if (anchors.Length == 0)
        {
            Debug.LogWarning("[SavePath] No anchors loaded. Aborting path save.");
            yield break;
        }

        var refAnchor = anchors[0].transform;
        Vector3 refPos = refAnchor.position;
        Quaternion refRot = refAnchor.rotation;
        Debug.Log($"[SavePath] Using anchor at {refPos} as reference.");

        string pathName = GenerateRandomName(4);
        var data = new PathData { pathName = pathName, points = new List<NavPointInfo>() };
        foreach (var go in navPoints)
        {
            Vector3 relPos = Quaternion.Inverse(refRot) * (go.transform.position - refPos);
            Quaternion relQuat = Quaternion.Inverse(refRot) * go.transform.rotation;
            data.points.Add(new NavPointInfo
            {
                relX = relPos.x,
                relY = relPos.y,
                relZ = relPos.z,
                relQx = relQuat.x,
                relQy = relQuat.y,
                relQz = relQuat.z,
                relQw = relQuat.w
            });
        }

        string json = JsonUtility.ToJson(data, true);
        string filePath = Path.Combine(Application.persistentDataPath, pathName + ".json");
        try
        {
            File.WriteAllText(filePath, json, Encoding.UTF8);
            Debug.Log($"[SavePath] Saved path '{pathName}' with {data.points.Count} points.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[SavePath] Failed to save path: {e}");
        }

        foreach (var anchor in FindObjectsOfType<OVRSpatialAnchor>())
            Destroy(anchor.gameObject);
        DeleteAllNavPoints();
        Debug.Log("[SavePath] Cleared anchors and navigation points after save.");
    }

    public void LoadPathMenu()
    {
        if (handMenu == null)
        {
            Debug.LogWarning("HandMenu is not assigned.");
            return;
        }
        bool isActive = !handMenu.activeSelf;
        handMenu.SetActive(isActive);
        if (isActive)
        {
            PopulateDropdown();
            if (loadButton != null)
                loadButton.interactable = pathDropdown.options.Count > 0;
        }
    }

    private void OnLoadButtonClicked()
    {
        StartCoroutine(LoadPathCoroutine());
    }

    private IEnumerator LoadPathCoroutine()
    {
        SpatialAnchorManager.Instance.ClearAnchors();
        DeleteAllNavPoints();
        Debug.Log("[LoadPath] Cleared existing anchors and nav points.");

        string anchorsJson = Path.Combine(Application.persistentDataPath, "anchors.json");
        if (!File.Exists(anchorsJson))
        {
            Debug.LogWarning("[LoadPath] No anchors.json found—aborting load.");
            yield break;
        }

        bool anchorsLoaded = false;
        List<OVRSpatialAnchor> loadedAnchors = null;
        void OnAnchorsLoaded(List<OVRSpatialAnchor> list)
        {
            anchorsLoaded = true;
            loadedAnchors = list;
        }
        SpatialAnchorManager.Instance.OnAnchorsLoaded += OnAnchorsLoaded;
        SpatialAnchorManager.Instance.LoadAnchorsFromJson();
        float timer = 0f, timeout = 5f;
        while (!anchorsLoaded && timer < timeout)
        {
            timer += Time.deltaTime;
            yield return null;
        }
        SpatialAnchorManager.Instance.OnAnchorsLoaded -= OnAnchorsLoaded;

        if (!anchorsLoaded || loadedAnchors == null || loadedAnchors.Count == 0)
        {
            Debug.LogWarning("[LoadPath] Failed to load any anchors—aborting nav-point load.");
            yield break;
        }

        Transform refT = loadedAnchors[0].transform;
        Vector3 refPos = refT.position;
        Quaternion refRot = refT.rotation;
        Debug.Log($"[LoadPath] Using anchor at {refPos} as reference.");

        string selectedName = pathDropdown.options[pathDropdown.value].text;
        string pathFile = Path.Combine(Application.persistentDataPath, selectedName + ".json");
        if (!File.Exists(pathFile))
        {
            Debug.LogError($"[LoadPath] Path file not found: {pathFile}");
            yield break;
        }
        string json = File.ReadAllText(pathFile, Encoding.UTF8);
        var data = JsonUtility.FromJson<PathData>(json);
        if (data == null || data.points == null)
        {
            Debug.LogError("[LoadPath] Failed to parse path JSON.");
            yield break;
        }

        foreach (var info in data.points)
        {
            Vector3 worldPos = refRot * new Vector3(info.relX, info.relY, info.relZ) + refPos;
            Quaternion worldRot = refRot * new Quaternion(info.relQx, info.relQy, info.relQz, info.relQw);
            var go = Instantiate(navPointPrefab, worldPos, worldRot);
            navPoints.Add(go);
            latestNavPoint = go;
        }

        Debug.Log($"[LoadPath] Instantiated {navPoints.Count} nav points.");
    }

    private void PopulateDropdown()
    {
        if (pathDropdown == null)
        {
            Debug.LogWarning("PathDropdown is not assigned.");
            return;
        }
        var files = Directory.GetFiles(Application.persistentDataPath, "*.json");
        var options = new List<string>();
        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            if (name.Equals("anchors.json", StringComparison.OrdinalIgnoreCase))
                continue;
            options.Add(Path.GetFileNameWithoutExtension(name));
        }
        pathDropdown.ClearOptions();
        pathDropdown.AddOptions(options);
    }

    private void ClearPathsMenu()
    {
        var files = Directory.GetFiles(Application.persistentDataPath, "*.json");
        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            if (name.Equals("anchors.json", StringComparison.OrdinalIgnoreCase))
                continue;
            try { File.Delete(file); }
            catch (Exception e) { Debug.LogError($"Failed to delete {file}: {e}"); }
        }
        PopulateDropdown();
        if (loadButton != null)
            loadButton.interactable = pathDropdown.options.Count > 0;
    }

    private string GenerateRandomName(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var sb = new StringBuilder(length);
        for (int i = 0; i < length; i++)
            sb.Append(chars[UnityEngine.Random.Range(0, chars.Length)]);
        return sb.ToString();
    }

    public GameObject GetLatestNavPoint() => latestNavPoint;
    public IReadOnlyList<GameObject> GetAllNavPoints() => navPoints.AsReadOnly();

    public void StartGame()
    {
        if (SpatialAnchorManager.Instance != null && SpatialAnchorManager.Instance.HasAnchorInScene())
        {
            SpatialAnchorManager.Instance.ClearOnlyAnchorPrefabs();
        }

        if (navPoints.Count > 0)
            DeleteAllNavPoints();

        if (qrScanner != null)
        {
            qrScanner.OnQRScanned += HandleQRScanned;
            qrScanner.scanningEnabled = true;
        }
        else
        {
            Debug.LogError("QRPhotoScanner reference not set!");
        }
    }

    private void HandleQRScanned(string qrMessage)
    {
        if (qrScanner != null)
        {
            qrScanner.scanningEnabled = false;
            qrScanner.OnQRScanned -= HandleQRScanned;
        }

        Debug.Log($"[CreatePathManager] QR scanned: {qrMessage}");

        if (!string.IsNullOrWhiteSpace(qrMessage))
        {
            string_start = qrMessage;
            game_start = true;
        }
        else
        {
            Debug.LogWarning("[CreatePathManager] Scanned QR code is empty or invalid.");
        }
    }
}






//using System;
//using System.Collections;
//using System.Collections.Generic;
//using System.IO;
//using System.Text;
//using UnityEngine;
//using UnityEngine.UI;
//using TMPro;

//public class CreatePathManager : MonoBehaviour
//{
//    [Header("Prefabs & Anchors")]
//    public GameObject navPointPrefab;
//    public Transform rightHandAnchor;

//    [Header("UI Elements")]
//    public GameObject handMenu;
//    public TMP_Dropdown pathDropdown;
//    public Button loadButton;

//    [Header("QR Scanner")]
//    public QRPhotoScanner qrScanner;

//    private List<GameObject> navPoints = new List<GameObject>();
//    private GameObject latestNavPoint;

//    public string string_start = " ";
//    public bool game_start = false;

//    void Start()
//    {
//        if (loadButton != null)
//            loadButton.onClick.AddListener(OnLoadButtonClicked);
//    }

//    public void CallPoint(int sliceIndex)
//    {
//        switch (sliceIndex)
//        {
//            case 0: CreateNavPoint(); break;
//            case 1: DeleteNavPoint(); break;
//            case 2: DeleteAllNavPoints(); break;
//            case 3: SavePath(); break;
//            case 4: LoadPathMenu(); break;
//            case 5: ClearPathsMenu(); break;
//            case 6: StartGame(); break;           // New start-game case
//            default:
//                Debug.LogWarning($"[CreatePathManager] No action for slice {sliceIndex}");
//                break;
//        }
//    }

//    public void CreateNavPoint()
//    {
//        if (navPointPrefab == null)
//        {
//            Debug.LogError("NavPoint prefab is not assigned.");
//            return;
//        }
//        Vector3 pos = rightHandAnchor != null ? rightHandAnchor.position : transform.position;
//        Quaternion rot = rightHandAnchor != null ? rightHandAnchor.rotation : transform.rotation;
//        var go = Instantiate(navPointPrefab, pos, rot);
//        navPoints.Add(go);
//        latestNavPoint = go;
//    }

//    public void DeleteNavPoint()
//    {
//        if (navPoints.Count == 0)
//        {
//            Debug.LogWarning("No navigation points to delete.");
//            latestNavPoint = null;
//            return;
//        }
//        int i = navPoints.Count - 1;
//        Destroy(navPoints[i]);
//        navPoints.RemoveAt(i);
//        latestNavPoint = navPoints.Count > 0 ? navPoints[^1] : null;
//    }

//    public void DeleteAllNavPoints()
//    {
//        for (int i = navPoints.Count - 1; i >= 0; i--)
//            Destroy(navPoints[i]);
//        navPoints.Clear();
//        latestNavPoint = null;
//    }

//    public void SavePath()
//    {
//        if (navPoints.Count == 0)
//        {
//            Debug.LogWarning("No navigation points to save.");
//            return;
//        }
//        StartCoroutine(SavePathCoroutine());
//    }

//    private IEnumerator SavePathCoroutine()
//    {
//        // 1) Clear any existing anchors in the scene
//        foreach (var anchor in FindObjectsOfType<OVRSpatialAnchor>())
//            Destroy(anchor.gameObject);
//        Debug.Log("[SavePath] Cleared existing anchors from scene.");

//        // 2) Load anchors from anchors.json
//        SpatialAnchorManager.Instance.LoadAnchorsFromJson();
//        Debug.Log("[SavePath] Loading anchors from anchors.json...");

//        // Wait up to 5 seconds for anchors to appear
//        float timeout = 5f, timer = 0f;
//        while (FindObjectsOfType<OVRSpatialAnchor>().Length == 0 && timer < timeout)
//        {
//            timer += Time.deltaTime;
//            yield return null;
//        }

//        var anchors = FindObjectsOfType<OVRSpatialAnchor>();
//        if (anchors.Length == 0)
//        {
//            Debug.LogWarning("[SavePath] No anchors loaded. Aborting path save.");
//            yield break;
//        }

//        // 3) Use first anchor as reference
//        var refAnchor = anchors[0].transform;
//        Vector3 refPos = refAnchor.position;
//        Quaternion refRot = refAnchor.rotation;
//        Debug.Log($"[SavePath] Using anchor at {refPos} as reference.");

//        // Build PathData
//        string pathName = GenerateRandomName(4);
//        var data = new PathData { pathName = pathName, points = new List<NavPointInfo>() };
//        foreach (var go in navPoints)
//        {
//            Vector3 relPos = Quaternion.Inverse(refRot) * (go.transform.position - refPos);
//            Quaternion relQuat = Quaternion.Inverse(refRot) * go.transform.rotation;
//            data.points.Add(new NavPointInfo
//            {
//                relX = relPos.x,
//                relY = relPos.y,
//                relZ = relPos.z,
//                relQx = relQuat.x,
//                relQy = relQuat.y,
//                relQz = relQuat.z,
//                relQw = relQuat.w
//            });
//        }

//        // 4) Write JSON
//        string json = JsonUtility.ToJson(data, true);
//        string filePath = Path.Combine(Application.persistentDataPath, pathName + ".json");
//        try
//        {
//            File.WriteAllText(filePath, json, Encoding.UTF8);
//            Debug.Log($"[SavePath] Saved path '{pathName}' with {data.points.Count} points.");
//        }
//        catch (Exception e)
//        {
//            Debug.LogError($"[SavePath] Failed to save path: {e}");
//        }

//        // 5) Clear anchors & nav points
//        foreach (var anchor in FindObjectsOfType<OVRSpatialAnchor>())
//            Destroy(anchor.gameObject);
//        DeleteAllNavPoints();
//        Debug.Log("[SavePath] Cleared anchors and navigation points after save.");
//    }

//    public void LoadPathMenu()
//    {
//        if (handMenu == null)
//        {
//            Debug.LogWarning("HandMenu is not assigned.");
//            return;
//        }
//        bool isActive = !handMenu.activeSelf;
//        handMenu.SetActive(isActive);
//        if (isActive)
//        {
//            PopulateDropdown();
//            if (loadButton != null)
//                loadButton.interactable = pathDropdown.options.Count > 0;
//        }
//    }

//    private void OnLoadButtonClicked()
//    {
//        StartCoroutine(LoadPathCoroutine());
//    }

//    private IEnumerator LoadPathCoroutine()
//    {
//        // 1) Clear any anchors & nav points in the scene
//        SpatialAnchorManager.Instance.ClearAnchors();
//        DeleteAllNavPoints();
//        Debug.Log("[LoadPath] Cleared existing anchors and nav points.");

//        // 2) Ensure anchors.json exists
//        string anchorsJson = Path.Combine(Application.persistentDataPath, "anchors.json");
//        if (!File.Exists(anchorsJson))
//        {
//            Debug.LogWarning("[LoadPath] No anchors.json found—aborting load.");
//            yield break;
//        }

//        // 3) Wait for anchors to load via event
//        bool anchorsLoaded = false;
//        List<OVRSpatialAnchor> loadedAnchors = null;
//        void OnAnchorsLoaded(List<OVRSpatialAnchor> list)
//        {
//            anchorsLoaded = true;
//            loadedAnchors = list;
//        }
//        SpatialAnchorManager.Instance.OnAnchorsLoaded += OnAnchorsLoaded;
//        SpatialAnchorManager.Instance.LoadAnchorsFromJson();
//        float timer = 0f, timeout = 5f;
//        while (!anchorsLoaded && timer < timeout)
//        {
//            timer += Time.deltaTime;
//            yield return null;
//        }
//        SpatialAnchorManager.Instance.OnAnchorsLoaded -= OnAnchorsLoaded;

//        if (!anchorsLoaded || loadedAnchors == null || loadedAnchors.Count == 0)
//        {
//            Debug.LogWarning("[LoadPath] Failed to load any anchors—aborting nav-point load.");
//            yield break;
//        }

//        // 4) Use first anchor as reference
//        Transform refT = loadedAnchors[0].transform;
//        Vector3 refPos = refT.position;
//        Quaternion refRot = refT.rotation;
//        Debug.Log($"[LoadPath] Using anchor at {refPos} as reference.");

//        // 5) Load nav-point JSON
//        string selectedName = pathDropdown.options[pathDropdown.value].text;
//        string pathFile = Path.Combine(Application.persistentDataPath, selectedName + ".json");
//        if (!File.Exists(pathFile))
//        {
//            Debug.LogError($"[LoadPath] Path file not found: {pathFile}");
//            yield break;
//        }
//        string json = File.ReadAllText(pathFile, Encoding.UTF8);
//        var data = JsonUtility.FromJson<PathData>(json);
//        if (data == null || data.points == null)
//        {
//            Debug.LogError("[LoadPath] Failed to parse path JSON.");
//            yield break;
//        }

//        // 6) Instantiate nav points in world space
//        foreach (var info in data.points)
//        {
//            Vector3 worldPos = refRot * new Vector3(info.relX, info.relY, info.relZ) + refPos;
//            Quaternion worldRot = refRot * new Quaternion(info.relQx, info.relQy, info.relQz, info.relQw);
//            var go = Instantiate(navPointPrefab, worldPos, worldRot);
//            navPoints.Add(go);
//            latestNavPoint = go;
//        }

//        Debug.Log($"[LoadPath] Instantiated {navPoints.Count} nav points.");
//    }

//    private void PopulateDropdown()
//    {
//        if (pathDropdown == null)
//        {
//            Debug.LogWarning("PathDropdown is not assigned.");
//            return;
//        }
//        var files = Directory.GetFiles(Application.persistentDataPath, "*.json");
//        var options = new List<string>();
//        foreach (var file in files)
//        {
//            var name = Path.GetFileName(file);
//            if (name.Equals("anchors.json", StringComparison.OrdinalIgnoreCase))
//                continue;
//            options.Add(Path.GetFileNameWithoutExtension(name));
//        }
//        pathDropdown.ClearOptions();
//        pathDropdown.AddOptions(options);
//    }

//    private void ClearPathsMenu()
//    {
//        var files = Directory.GetFiles(Application.persistentDataPath, "*.json");
//        foreach (var file in files)
//        {
//            var name = Path.GetFileName(file);
//            if (name.Equals("anchors.json", StringComparison.OrdinalIgnoreCase))
//                continue;
//            try { File.Delete(file); }
//            catch (Exception e) { Debug.LogError($"Failed to delete {file}: {e}"); }
//        }
//        PopulateDropdown();
//        if (loadButton != null)
//            loadButton.interactable = pathDropdown.options.Count > 0;
//    }

//    private string GenerateRandomName(int length)
//    {
//        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
//        var sb = new StringBuilder(length);
//        for (int i = 0; i < length; i++)
//            sb.Append(chars[UnityEngine.Random.Range(0, chars.Length)]);
//        return sb.ToString();
//    }

//    public GameObject GetLatestNavPoint() => latestNavPoint;
//    public IReadOnlyList<GameObject> GetAllNavPoints() => navPoints.AsReadOnly();

//    /// <summary>
//    /// Clears anchors and nav points, then enables QR scanning.
//    /// </summary>
//    //public void StartGame()
//    //{
//    //    // Clear all spatial anchors if any exist
//    //    if (SpatialAnchorManager.Instance != null)
//    //        SpatialAnchorManager.Instance.ClearAnchors();

//    //    // Clear all nav points if any exist
//    //    if (navPoints.Count > 0)
//    //        DeleteAllNavPoints();

//    //    // Begin QR scanning
//    //    if (qrScanner != null)
//    //    {
//    //        qrScanner.OnQRScanned += HandleQRScanned;
//    //        qrScanner.scanningEnabled = true;
//    //    }
//    //    else
//    //    {
//    //        Debug.LogError("QRPhotoScanner reference not set!");
//    //        return;
//    //    }

//    //    string_start = qrScanner.resultText;
//    //    game_start = true;
//    //}

//    ///// <summary>
//    ///// Called when a valid QR code is decoded.
//    ///// </summary>
//    //private void HandleQRScanned(string qrMessage)
//    //{
//    //    // Stop further scanning
//    //    if (qrScanner != null)
//    //    {
//    //        qrScanner.scanningEnabled = false;
//    //        qrScanner.OnQRScanned -= HandleQRScanned;
//    //    }

//    //    Debug.Log($"[CreatePathManager] QR scanned: {qrMessage}");
//    //    // TODO: Add logic to handle the scanned message (e.g., start path creation using qrMessage)
//    //}


//    public void StartGame()
//    {
//        // Clear anchors if any exist in the scene via SpatialAnchorManager
//        //if (SpatialAnchorManager.Instance != null &&
//        //    SpatialAnchorManager.Instance.HasAnchorsInScene())
//        //{
//        //    SpatialAnchorManager.Instance.ClearAnchors();
//        //}

//        //// Clear nav points if any exist
//        //if (navPoints.Count > 0)
//        //    DeleteAllNavPoints();

//        //// Begin QR scanning
//        //if (qrScanner != null)
//        //{
//        //    qrScanner.OnQRScanned += HandleQRScanned;
//        //    qrScanner.scanningEnabled = true;
//        //}
//        //else
//        //{
//        //    Debug.LogError("QRPhotoScanner reference not set!");
//        //}


//    }




//    private void HandleQRScanned(string qrMessage)
//    {
//        if (qrScanner != null)
//        {
//            qrScanner.scanningEnabled = false;
//            qrScanner.OnQRScanned -= HandleQRScanned;
//        }

//        Debug.Log($"[CreatePathManager] QR scanned: {qrMessage}");

//        if (!string.IsNullOrWhiteSpace(qrMessage))
//        {
//            string_start = qrMessage;
//            game_start = true; // This will now properly trigger GameManager
//        }
//        else
//        {
//            Debug.LogWarning("[CreatePathManager] Scanned QR code is empty or invalid.");
//        }
//    }





//}
