using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    [Header("Dependencies")]
    public CreatePathManager pathManager;
    public GameObject navPointPrefab;

    [Header("Collider Control")]
    [SerializeField] private Collider handCollider; // can be assigned manually; falls back to GetComponent<Collider>()

    private List<GameObject> navPoints = new List<GameObject>();
    private GameObject firstNavPoint;
    private GameObject latestNavPoint;
    private bool hasInitialized = false;

    // Buttons to poll (digital)
    private static readonly OVRInput.Button[] _buttonsToCheck = new[]
    {
        OVRInput.Button.One,
        OVRInput.Button.Two,
        OVRInput.Button.Three,
        OVRInput.Button.Four,
        OVRInput.Button.Start,
        OVRInput.Button.Back,
        OVRInput.Button.PrimaryThumbstick,
        OVRInput.Button.SecondaryThumbstick,
        OVRInput.Button.PrimaryIndexTrigger,
        OVRInput.Button.SecondaryIndexTrigger,
        OVRInput.Button.PrimaryShoulder,
        OVRInput.Button.SecondaryShoulder,
        OVRInput.Button.PrimaryHandTrigger,
        OVRInput.Button.SecondaryHandTrigger
    };

    // Keep track of original colors so we can restore on exit
    private readonly Dictionary<Renderer, Color> _originalColors = new();

    void Awake()
    {
        if (handCollider == null)
            handCollider = GetComponent<Collider>();

        if (handCollider == null)
            Debug.LogWarning("[GameManager] No collider found on this GameObject or assigned to handCollider.");
    }

    void Update()
    {
        UpdateColliderState();

        if (hasInitialized || pathManager == null)
            return;

        string startCode = pathManager.string_start;
        bool started = pathManager.game_start;
        bool isValidStart = !string.IsNullOrWhiteSpace(startCode);

        if (started && isValidStart)
        {
            hasInitialized = true;
            Debug.Log("[GameManager] Game start conditions met. Beginning nav point loading...");
            StartCoroutine(LoadNavPointsFromFile(startCode));
        }
    }

    private void UpdateColliderState()
    {
        if (handCollider == null)
            return;

        bool anyButtonDown = false;

        // Digital buttons
        foreach (var btn in _buttonsToCheck)
        {
            if (OVRInput.Get(btn))
            {
                anyButtonDown = true;
                break;
            }
        }

        // Analog triggers / grips
        if (!anyButtonDown)
        {
            if (OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger) > 0.1f ||
                OVRInput.Get(OVRInput.Axis1D.SecondaryIndexTrigger) > 0.1f ||
                OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger) > 0.1f ||
                OVRInput.Get(OVRInput.Axis1D.SecondaryHandTrigger) > 0.1f)
            {
                anyButtonDown = true;
            }
        }

        bool desiredEnabled = !anyButtonDown;
        if (handCollider.enabled != desiredEnabled)
            handCollider.enabled = desiredEnabled;
    }

    private IEnumerator LoadNavPointsFromFile(string fileName)
    {
        string anchorsJson = Path.Combine(Application.persistentDataPath, "anchors.json");
        if (!File.Exists(anchorsJson))
            yield break;

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
            yield break;

        Transform refT = loadedAnchors[0].transform;
        Vector3 refPos = refT.position;
        Quaternion refRot = refT.rotation;

        string pathFile = Path.Combine(Application.persistentDataPath, fileName + ".json");
        if (!File.Exists(pathFile))
            yield break;

        string json = File.ReadAllText(pathFile, Encoding.UTF8);
        var data = JsonUtility.FromJson<PathData>(json);
        if (data == null || data.points == null)
            yield break;

        for (int i = 0; i < data.points.Count; i++)
        {
            var info = data.points[i];
            Vector3 worldPos = refRot * new Vector3(info.relX, info.relY, info.relZ) + refPos;
            Quaternion worldRot = refRot * new Quaternion(info.relQx, info.relQy, info.relQz, info.relQw);

            var go = Instantiate(navPointPrefab, worldPos, worldRot);
            navPoints.Add(go);
            latestNavPoint = go;

            if (i == 0)
                firstNavPoint = go;
        }



        Debug.Log($"[GameManager] Loaded and instantiated {navPoints.Count} nav points from '{fileName}.json'");
    }

    private void ClearExistingNavPoints()
    {
        foreach (var point in navPoints)
            if (point != null) Destroy(point);
        navPoints.Clear();
        firstNavPoint = null;
        latestNavPoint = null;
    }

    void OnTriggerEnter(Collider other)
    {


        // New TEST tag logic: change material color to green
        GameObject testObject = null;
        if (other.CompareTag("TEST"))
            testObject = other.gameObject;
        else if (other.transform.parent != null && other.transform.parent.CompareTag("TEST"))
            testObject = other.transform.parent.gameObject;

        if (testObject != null)
        {
            var renderers = testObject.GetComponentsInChildren<Renderer>();
            foreach (var rend in renderers)
            {
                if (!_originalColors.ContainsKey(rend))
                    _originalColors[rend] = rend.material.color;
                rend.material.color = Color.green;
            }
        }
    }

    void OnTriggerExit(Collider other)
    {
        // Restore original color if we exited a TEST object
        GameObject testObject = null;
        if (other.CompareTag("TEST"))
            testObject = other.gameObject;
        else if (other.transform.parent != null && other.transform.parent.CompareTag("TEST"))
            testObject = other.transform.parent.gameObject;

        if (testObject != null)
        {
            var renderers = testObject.GetComponentsInChildren<Renderer>();
            foreach (var rend in renderers)
            {
                if (_originalColors.TryGetValue(rend, out var original))
                {
                    rend.material.color = original;
                    _originalColors.Remove(rend);
                }
            }
        }
    }

    public GameObject GetFirstNavPoint() => firstNavPoint;
    public GameObject GetLatestNavPoint() => latestNavPoint;
    public IReadOnlyList<GameObject> GetAllNavPoints() => navPoints.AsReadOnly();
}





//using System.Collections;
//using System.Collections.Generic;
//using System.IO;
//using System.Text;
//using UnityEngine;

//public class GameManager : MonoBehaviour
//{
//    [Header("Dependencies")]
//    public CreatePathManager pathManager;
//    public GameObject navPointPrefab;

//    [Header("Collider Control")]
//    [SerializeField] private Collider handCollider; // can be assigned manually; falls back to GetComponent<Collider>()

//    private List<GameObject> navPoints = new List<GameObject>();
//    private GameObject firstNavPoint;
//    private GameObject latestNavPoint;
//    private bool hasInitialized = false;

//    // Buttons to poll (digital)
//    private static readonly OVRInput.Button[] _buttonsToCheck = new[]
//    {
//        OVRInput.Button.One,
//        OVRInput.Button.Two,
//        OVRInput.Button.Three,
//        OVRInput.Button.Four,
//        OVRInput.Button.Start,
//        OVRInput.Button.Back,
//        OVRInput.Button.PrimaryThumbstick,
//        OVRInput.Button.SecondaryThumbstick,
//        OVRInput.Button.PrimaryIndexTrigger,
//        OVRInput.Button.SecondaryIndexTrigger,
//        OVRInput.Button.PrimaryShoulder,
//        OVRInput.Button.SecondaryShoulder,
//        OVRInput.Button.PrimaryHandTrigger,
//        OVRInput.Button.SecondaryHandTrigger
//    };

//    // Keep track of original colors so we can restore on exit
//    private readonly Dictionary<Renderer, Color> _originalColors = new();

//    void Awake()
//    {
//        if (handCollider == null)
//            handCollider = GetComponent<Collider>();

//        if (handCollider == null)
//            Debug.LogWarning("[GameManager] No collider found on this GameObject or assigned to handCollider.");
//    }

//    void Update()
//    {
//        UpdateColliderState();

//        if (hasInitialized || pathManager == null)
//            return;

//        string startCode = pathManager.string_start;
//        bool started = pathManager.game_start;
//        bool isValidStart = !string.IsNullOrWhiteSpace(startCode);

//        if (started && isValidStart)
//        {
//            hasInitialized = true;
//            Debug.Log("[GameManager] Game start conditions met. Beginning nav point loading...");
//            StartCoroutine(LoadNavPointsFromFile(startCode));
//        }
//    }

//    private void UpdateColliderState()
//    {
//        if (handCollider == null)
//            return;

//        bool anyButtonDown = false;

//        // Digital buttons
//        foreach (var btn in _buttonsToCheck)
//        {
//            if (OVRInput.Get(btn))
//            {
//                anyButtonDown = true;
//                break;
//            }
//        }

//        // Analog triggers / grips
//        if (!anyButtonDown)
//        {
//            if (OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger) > 0.1f ||
//                OVRInput.Get(OVRInput.Axis1D.SecondaryIndexTrigger) > 0.1f ||
//                OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger) > 0.1f ||
//                OVRInput.Get(OVRInput.Axis1D.SecondaryHandTrigger) > 0.1f)
//            {
//                anyButtonDown = true;
//            }
//        }

//        bool desiredEnabled = !anyButtonDown;
//        if (handCollider.enabled != desiredEnabled)
//            handCollider.enabled = desiredEnabled;
//    }

//    private IEnumerator LoadNavPointsFromFile(string fileName)
//    {
//        string anchorsJson = Path.Combine(Application.persistentDataPath, "anchors.json");
//        if (!File.Exists(anchorsJson))
//            yield break;

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
//            yield break;

//        Transform refT = loadedAnchors[0].transform;
//        Vector3 refPos = refT.position;
//        Quaternion refRot = refT.rotation;

//        string pathFile = Path.Combine(Application.persistentDataPath, fileName + ".json");
//        if (!File.Exists(pathFile))
//            yield break;

//        string json = File.ReadAllText(pathFile, Encoding.UTF8);
//        var data = JsonUtility.FromJson<PathData>(json);
//        if (data == null || data.points == null)
//            yield break;

//        for (int i = 0; i < data.points.Count; i++)
//        {
//            var info = data.points[i];
//            Vector3 worldPos = refRot * new Vector3(info.relX, info.relY, info.relZ) + refPos;
//            Quaternion worldRot = refRot * new Quaternion(info.relQx, info.relQy, info.relQz, info.relQw);

//            var go = Instantiate(navPointPrefab, worldPos, worldRot);
//            navPoints.Add(go);
//            latestNavPoint = go;

//            if (i == 0)
//                firstNavPoint = go;
//        }

//        for (int i = 0; i < navPoints.Count; i++)
//        {
//            var pointChild = navPoints[i].transform.Find("Point");
//            if (pointChild != null)
//                pointChild.gameObject.SetActive(i == 0);
//        }

//        Debug.Log($"[GameManager] Loaded and instantiated {navPoints.Count} nav points from '{fileName}.json'");
//    }

//    private void ClearExistingNavPoints()
//    {
//        foreach (var point in navPoints)
//            if (point != null) Destroy(point);
//        navPoints.Clear();
//        firstNavPoint = null;
//        latestNavPoint = null;
//    }

//    void OnTriggerEnter(Collider other)
//    {
//        // Existing nav point logic (parent tagged "Unmarked")
//        Transform navParent = other.transform.parent;
//        if (navParent != null && navParent.CompareTag("Unmarked"))
//        {
//            Transform currentPoint = navParent.Find("Point");
//            if (currentPoint != null)
//                currentPoint.gameObject.SetActive(false);

//            navParent.tag = "Marked";

//            int index = navPoints.IndexOf(navParent.gameObject);
//            if (index >= 0 && index + 1 < navPoints.Count)
//            {
//                Transform nextPoint = navPoints[index + 1].transform.Find("Point");
//                if (nextPoint != null)
//                    nextPoint.gameObject.SetActive(true);
//            }
//        }

//        // New TEST tag logic: change material color to green
//        GameObject testObject = null;
//        if (other.CompareTag("TEST"))
//            testObject = other.gameObject;
//        else if (other.transform.parent != null && other.transform.parent.CompareTag("TEST"))
//            testObject = other.transform.parent.gameObject;

//        if (testObject != null)
//        {
//            var renderers = testObject.GetComponentsInChildren<Renderer>();
//            foreach (var rend in renderers)
//            {
//                if (!_originalColors.ContainsKey(rend))
//                    _originalColors[rend] = rend.material.color;
//                rend.material.color = Color.green;
//            }
//        }
//    }

//    void OnTriggerExit(Collider other)
//    {
//        // Restore original color if we exited a TEST object
//        GameObject testObject = null;
//        if (other.CompareTag("TEST"))
//            testObject = other.gameObject;
//        else if (other.transform.parent != null && other.transform.parent.CompareTag("TEST"))
//            testObject = other.transform.parent.gameObject;

//        if (testObject != null)
//        {
//            var renderers = testObject.GetComponentsInChildren<Renderer>();
//            foreach (var rend in renderers)
//            {
//                if (_originalColors.TryGetValue(rend, out var original))
//                {
//                    rend.material.color = original;
//                    _originalColors.Remove(rend);
//                }
//            }
//        }
//    }

//    public GameObject GetFirstNavPoint() => firstNavPoint;
//    public GameObject GetLatestNavPoint() => latestNavPoint;
//    public IReadOnlyList<GameObject> GetAllNavPoints() => navPoints.AsReadOnly();
//}




//using System.Collections;
//using System.Collections.Generic;
//using System.IO;
//using System.Text;
//using UnityEngine;

//public class GameManager : MonoBehaviour
//{
//    [Header("Dependencies")]
//    public CreatePathManager pathManager;
//    public GameObject navPointPrefab;

//    private List<GameObject> navPoints = new List<GameObject>();
//    private GameObject firstNavPoint;
//    private GameObject latestNavPoint;
//    private bool hasInitialized = false;

//    void Update()
//    {
//        if (hasInitialized || pathManager == null)
//            return;

//        string startCode = pathManager.string_start;
//        bool started = pathManager.game_start;
//        bool isValidStart = !string.IsNullOrWhiteSpace(startCode);

//        if (started && isValidStart)
//        {
//            hasInitialized = true;
//            Debug.Log("[GameManager] Game start conditions met. Beginning nav point loading...");
//            StartCoroutine(LoadNavPointsFromFile(startCode));
//        }
//    }

//    private IEnumerator LoadNavPointsFromFile(string fileName)
//    {
//        // (Optional) clear any existing anchors/points here if needed:
//        // SpatialAnchorManager.Instance?.ClearAnchors();
//        // ClearExistingNavPoints();

//        // 1. Load anchors.json
//        string anchorsJson = Path.Combine(Application.persistentDataPath, "anchors.json");
//        if (!File.Exists(anchorsJson))
//        {
//            Debug.LogWarning("[GameManager] No anchors.json found—aborting nav point load.");
//            yield break;
//        }

//        // 2. Load anchors via callback
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
//            Debug.LogWarning("[GameManager] No anchors loaded from anchors.json.");
//            yield break;
//        }

//        // 3. Reference transform from first anchor
//        Transform refT = loadedAnchors[0].transform;
//        Vector3 refPos = refT.position;
//        Quaternion refRot = refT.rotation;

//        // 4. Read path data
//        string pathFile = Path.Combine(Application.persistentDataPath, fileName + ".json");
//        if (!File.Exists(pathFile))
//        {
//            Debug.LogError("[GameManager] Path file not found: " + pathFile);
//            yield break;
//        }

//        string json = File.ReadAllText(pathFile, Encoding.UTF8);
//        var data = JsonUtility.FromJson<PathData>(json);
//        if (data == null || data.points == null)
//        {
//            Debug.LogError("[GameManager] Failed to parse nav-point data.");
//            yield break;
//        }

//        // 5. Instantiate nav points
//        for (int i = 0; i < data.points.Count; i++)
//        {
//            var info = data.points[i];
//            Vector3 worldPos = refRot * new Vector3(info.relX, info.relY, info.relZ) + refPos;
//            Quaternion worldRot = refRot * new Quaternion(info.relQx, info.relQy, info.relQz, info.relQw);

//            var go = Instantiate(navPointPrefab, worldPos, worldRot);
//            navPoints.Add(go);
//            latestNavPoint = go;

//            if (i == 0)
//                firstNavPoint = go;
//        }

//        // 6. **Hide** the "Point" child on every nav?point except the first one
//        for (int i = 0; i < navPoints.Count; i++)
//        {
//            var pointParent = navPoints[i];
//            var pointChild = pointParent.transform.Find("Point");
//            if (pointChild != null)
//            {
//                // only leave the first point visible
//                pointChild.gameObject.SetActive(i == 0);
//            }
//        }

//        Debug.Log($"[GameManager] Loaded and instantiated {navPoints.Count} nav points from '{fileName}.json'");
//    }

//    private void ClearExistingNavPoints()
//    {
//        foreach (var point in navPoints)
//            if (point != null) Destroy(point);
//        navPoints.Clear();
//        firstNavPoint = null;
//        latestNavPoint = null;
//    }

//    public GameObject GetFirstNavPoint() => firstNavPoint;
//    public GameObject GetLatestNavPoint() => latestNavPoint;
//    public IReadOnlyList<GameObject> GetAllNavPoints() => navPoints.AsReadOnly();
//}





//using System.Collections;
//using System.Collections.Generic;
//using System.IO;
//using System.Text;
//using UnityEngine;

//public class GameManager : MonoBehaviour
//{
//    [Header("Dependencies")]
//    public CreatePathManager pathManager;
//    public GameObject navPointPrefab;

//    private List<GameObject> navPoints = new List<GameObject>();
//    private GameObject firstNavPoint;
//    private GameObject latestNavPoint;
//    private bool hasInitialized = false;

//    void Update()
//    {
//        if (hasInitialized || pathManager == null)
//            return;

//        string startCode = pathManager.string_start;
//        bool started = pathManager.game_start;
//        bool isValidStart = !string.IsNullOrWhiteSpace(startCode);

//        if (started && isValidStart)
//        {
//            hasInitialized = true;
//            Debug.Log("[GameManager] Game start conditions met. Beginning nav point loading...");
//            StartCoroutine(LoadNavPointsFromFile(startCode));
//        }
//    }

//    private IEnumerator LoadNavPointsFromFile(string fileName)
//    {



//        string anchorsJson = Path.Combine(Application.persistentDataPath, "anchors.json");
//        if (!File.Exists(anchorsJson))
//        {
//            Debug.LogWarning("[GameManager] No anchors.json found—aborting nav point load.");
//            yield break;
//        }

//        // 2. Load anchors
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
//            Debug.LogWarning("[GameManager] No anchors loaded from anchors.json.");
//            yield break;
//        }

//        // 3. Use first anchor as reference
//        Transform refT = loadedAnchors[0].transform;
//        Vector3 refPos = refT.position;
//        Quaternion refRot = refT.rotation;

//        // 4. Load nav point data from file
//        string pathFile = Path.Combine(Application.persistentDataPath, fileName + ".json");
//        if (!File.Exists(pathFile))
//        {
//            Debug.LogError("[GameManager] Path file not found: " + pathFile);
//            yield break;
//        }

//        string json = File.ReadAllText(pathFile, Encoding.UTF8);
//        var data = JsonUtility.FromJson<PathData>(json);
//        if (data == null || data.points == null)
//        {
//            Debug.LogError("[GameManager] Failed to parse nav-point data.");
//            yield break;
//        }

//        // 5. Instantiate nav points
//        for (int i = 0; i < data.points.Count; i++)
//        {
//            var info = data.points[i];
//            Vector3 worldPos = refRot * new Vector3(info.relX, info.relY, info.relZ) + refPos;
//            Quaternion worldRot = refRot * new Quaternion(info.relQx, info.relQy, info.relQz, info.relQw);

//            var go = Instantiate(navPointPrefab, worldPos, worldRot);
//            navPoints.Add(go);
//            latestNavPoint = go;

//            if (i == 0)
//                firstNavPoint = go;
//        }

//        Debug.Log($"[GameManager] Loaded and instantiated {navPoints.Count} nav points from '{fileName}.json'");
//    }

//    private void ClearExistingNavPoints()
//    {
//        foreach (var point in navPoints)
//            if (point != null) Destroy(point);
//        navPoints.Clear();
//        firstNavPoint = null;
//        latestNavPoint = null;
//    }

//    public GameObject GetFirstNavPoint() => firstNavPoint;
//    public GameObject GetLatestNavPoint() => latestNavPoint;
//    public IReadOnlyList<GameObject> GetAllNavPoints() => navPoints.AsReadOnly();
//}




//using System;
//using System.Collections;
//using System.Collections.Generic;
//using System.IO;
//using UnityEngine;

//public class GameManager : MonoBehaviour
//{
//    [Tooltip("Drag in the same CreatePathManager instance you're using elsewhere")]
//    public CreatePathManager pathManager;

//    [Tooltip("Prefab to use when instantiating nav points")]
//    public GameObject navPointPrefab;

//    [HideInInspector] public string string_start_GM;
//    [HideInInspector] public bool game_start_GM = false;

//    private List<GameObject> navPoints_GM = new List<GameObject>();
//    private GameObject latestNavPoint_GM;
//    private bool hasLoadedData = false;

//    void Update()
//    {
//        string_start_GM = pathManager.string_start;
//        game_start_GM = pathManager.game_start;

//        if (game_start_GM &&
//            !hasLoadedData &&
//            !string.IsNullOrEmpty(string_start_GM))
//        {
//            hasLoadedData = true;
//            StartCoroutine(LoadNavPointsFromPath(string_start_GM));
//        }
//    }

//    private IEnumerator LoadNavPointsFromPath(string pathName)
//    {
//        if (navPointPrefab == null)
//        {
//            Debug.LogError("[GameManager] NavPointPrefab is not assigned.");
//            yield break;
//        }

//        if (SpatialAnchorManager.Instance != null)
//            SpatialAnchorManager.Instance.ClearAnchors();
//        ClearExistingNavPoints();

//        // Load anchors
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
//            Debug.LogWarning("[GameManager] No anchors loaded from anchors.json.");
//            yield break;
//        }

//        // Load nav point data
//        string filePath = Path.Combine(Application.persistentDataPath, pathName + ".json");
//        if (!File.Exists(filePath))
//        {
//            Debug.LogError("[GameManager] NavPoint file not found: " + filePath);
//            yield break;
//        }

//        string json = File.ReadAllText(filePath);
//        var data = JsonUtility.FromJson<PathData>(json);
//        if (data == null || data.points == null)
//        {
//            Debug.LogError("[GameManager] Failed to parse nav-point data from " + filePath);
//            yield break;
//        }

//        // Instantiate nav points using the prefab
//        Transform refT = loadedAnchors[0].transform;
//        Vector3 refPos = refT.position;
//        Quaternion refRot = refT.rotation;

//        for (int i = 0; i < data.points.Count; i++)
//        {
//            var info = data.points[i];
//            Vector3 worldPos = refRot * new Vector3(info.relX, info.relY, info.relZ) + refPos;
//            Quaternion worldRot = refRot * new Quaternion(info.relQx, info.relQy, info.relQz, info.relQw);

//            GameObject go = Instantiate(navPointPrefab, worldPos, worldRot);
//            navPoints_GM.Add(go);
//            latestNavPoint_GM = go;

//            // Set up visibility and tagging
//            go.tag = "Unmarked"; // Tag is on the parent NavPoint

//            Transform pointChild = go.transform.Find("Point");
//            if (pointChild != null)
//            {
//                pointChild.gameObject.SetActive(i == 0); // Only the first point is visible
//            }
//            else
//            {
//                Debug.LogWarning($"[GameManager] Could not find 'Point' child on nav point {i}");
//            }
//        }

//        Debug.Log($"[GameManager] Instantiated {navPoints_GM.Count} nav-points from '{pathName}.json'.");
//    }

//    private void ClearExistingNavPoints()
//    {
//        for (int i = navPoints_GM.Count - 1; i >= 0; i--)
//            Destroy(navPoints_GM[i]);
//        navPoints_GM.Clear();
//        latestNavPoint_GM = null;
//    }

//    private void OnTriggerEnter(Collider other)
//    {
//        // Assume this is the child "Point" that collided
//        Transform parent = other.transform.parent;

//        if (parent != null && parent.CompareTag("Unmarked"))
//        {
//            parent.tag = "Marked";                  // Update tag on NavPoint
//            other.gameObject.SetActive(false);      // Hide the current Point

//            int index = GetNavPointIndexFromParent(parent);
//            int nextIndex = index + 1;

//            if (nextIndex < navPoints_GM.Count)
//            {
//                Transform nextChild = navPoints_GM[nextIndex].transform.Find("Point");
//                if (nextChild != null)
//                    nextChild.gameObject.SetActive(true); // Reveal the next nav point
//            }
//        }
//    }

//    private int GetNavPointIndexFromParent(Transform parent)
//    {
//        for (int i = 0; i < navPoints_GM.Count; i++)
//        {
//            if (navPoints_GM[i].transform == parent)
//                return i;
//        }
//        return -1;
//    }
//}
