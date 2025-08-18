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
    [SerializeField] private Collider handCollider;

    [Header("Audio")]
    public AudioClip unmarkedHitSound;
    [Range(0f, 1f)] public float unmarkedHitVolume = 1f;
    public AudioClip specialHitSound;
    [Range(0f, 1f)] public float specialHitVolume = 1f;
    private AudioSource _audioSource;

    [Header("Guidance / Instruction Audio")]
    public AudioClip reminderSound;
    [Range(0f, 1f)] public float reminderVolume = 1f;
    public float reminderInactivitySeconds = 7f;
    public AudioClip finalInstructionsSound;
    [Range(0f, 1f)] public float finalInstructionsVolume = 1f;

    [Header("Interaction Timing")]
    public float initialInteractionDelay = 1.0f;
    public float postRevealInteractionDelay = 0.25f;

    [Header("SPECIAL Feedback")]
    public GameObject thumbsUpPrefab;
    public float thumbsUpYOffset = 0.2f;
    public float thumbsUpLifetime = 2f;
    public float specialCleanupDelay = 3f;

    private List<GameObject> navPoints = new List<GameObject>();
    private GameObject firstNavPoint;
    private GameObject latestNavPoint;
    private bool hasInitialized = false;

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

    private readonly Dictionary<Renderer, Color> _originalColors = new();

    private Coroutine _reminderLoopCo;
    private Coroutine _priorityResetCo;
    private Coroutine _finalInstructionsCo;
    private float _lastTouchTime;
    private float _lastReminderTime;
    private bool _priorityActive = false;
    private bool _finalInstructionsQueued = false;

    private float _interactionUnlockTime = 0f;

    private AudioClip _currentReminderClip;
    private float _currentReminderVolume;
    private bool _useFinalReminder = false;
    private bool _remindersEnabled = false;

    private List<OVRSpatialAnchor> _loadedAnchors = new();

    void Awake()
    {
        if (handCollider == null)
            handCollider = GetComponent<Collider>();

        if (_audioSource == null)
            _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null)
            _audioSource = gameObject.AddComponent<AudioSource>();
        _audioSource.playOnAwake = false;
    }

    void Update()
    {
        UpdateColliderState();

        if (!hasInitialized && pathManager != null)
        {
            string startCode = pathManager.string_start;
            bool started = pathManager.game_start;
            bool isValidStart = !string.IsNullOrWhiteSpace(startCode);

            if (started && isValidStart)
            {
                hasInitialized = true;
                StartCoroutine(LoadNavPointsFromFile(startCode));
            }
        }
    }

    private void UpdateColliderState()
    {
        if (handCollider == null)
            return;

        bool anyButtonDown = false;

        foreach (var btn in _buttonsToCheck)
        {
            if (OVRInput.Get(btn))
            {
                anyButtonDown = true;
                break;
            }
        }

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
        ClearExistingNavPoints();

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

        _loadedAnchors = new List<OVRSpatialAnchor>(loadedAnchors);
        SetAnchorVisualsEnabled(_loadedAnchors, false);

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

            if (i > 0)
                go.SetActive(false);

            navPoints.Add(go);
            latestNavPoint = go;

            if (i == 0)
                firstNavPoint = go;
        }

        if (navPoints.Count > 0)
        {
            var last = navPoints[navPoints.Count - 1];
            last.tag = "SPECIAL";
            latestNavPoint = last;
        }

        _lastTouchTime = Time.time;
        _lastReminderTime = -999f;
        _finalInstructionsQueued = false;

        _currentReminderClip = reminderSound;
        _currentReminderVolume = reminderVolume;
        _useFinalReminder = false;
        _remindersEnabled = true;

        _interactionUnlockTime = Time.time + Mathf.Max(0f, initialInteractionDelay);

        if (_reminderLoopCo != null) StopCoroutine(_reminderLoopCo);
        _reminderLoopCo = StartCoroutine(ReminderLoop());

        yield return new WaitForSeconds(Mathf.Max(0f, initialInteractionDelay));
        TryPlayReminder();
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
        if (Time.time < _interactionUnlockTime)
            return;

        if (hasInitialized && pathManager != null && pathManager.game_start)
        {
            GameObject specialNavPoint = null;
            if (other.CompareTag("SPECIAL"))
                specialNavPoint = other.gameObject;
            else if (other.transform.parent != null && other.transform.parent.CompareTag("SPECIAL"))
                specialNavPoint = other.transform.parent.gameObject;

            if (specialNavPoint != null)
            {
                _lastTouchTime = Time.time;
                StartCoroutine(HandleSpecialNavPointHit(specialNavPoint));
                return;
            }

            GameObject navPoint = null;
            if (other.CompareTag("UNMARKED"))
                navPoint = other.gameObject;
            else if (other.transform.parent != null && other.transform.parent.CompareTag("UNMARKED"))
                navPoint = other.transform.parent.gameObject;

            if (navPoint != null)
            {
                _lastTouchTime = Time.time;

                if (unmarkedHitSound != null)
                    PlayPriorityClip(unmarkedHitSound, unmarkedHitVolume);

                var renderers = navPoint.GetComponentsInChildren<Renderer>();
                foreach (var rend in renderers)
                {
                    if (!_originalColors.ContainsKey(rend))
                        _originalColors[rend] = rend.material.color;
                    rend.material.color = Color.white;
                }

                foreach (Transform child in navPoint.transform)
                    child.gameObject.SetActive(false);

                navPoint.tag = "UNMARKED";

                var npCollider = navPoint.GetComponent<Collider>();
                if (npCollider != null)
                    npCollider.enabled = false;

                int currentIndex = navPoints.IndexOf(navPoint);
                if (currentIndex >= 0 && currentIndex + 1 < navPoints.Count)
                {
                    var next = navPoints[currentIndex + 1];
                    next.SetActive(true);
                    latestNavPoint = next;

                    _interactionUnlockTime = Time.time + Mathf.Max(0f, postRevealInteractionDelay);

                    if (!_finalInstructionsQueued && currentIndex == navPoints.Count - 2)
                    {
                        if (finalInstructionsSound != null)
                        {
                            _currentReminderClip = finalInstructionsSound;
                            _currentReminderVolume = finalInstructionsVolume;
                            _useFinalReminder = true;
                            _lastReminderTime = Time.time;
                        }

                        float delay = (unmarkedHitSound != null ? unmarkedHitSound.length : 0f) + 1f;
                        _finalInstructionsCo = StartCoroutine(PlayFinalInstructionsAfterDelay(delay));
                        _finalInstructionsQueued = true;
                    }
                }

                return;
            }
        }

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

    private IEnumerator HandleSpecialNavPointHit(GameObject navPoint)
    {
        if (specialHitSound != null)
            PlayPriorityClip(specialHitSound, specialHitVolume);

        var renderers = navPoint.GetComponentsInChildren<Renderer>();
        foreach (var rend in renderers)
        {
            if (!_originalColors.ContainsKey(rend))
                _originalColors[rend] = rend.material.color;
            rend.material.color = Color.white;
        }

        foreach (Transform child in navPoint.transform)
            child.gameObject.SetActive(false);

        navPoint.tag = "SPECIAL";

        var npCollider = navPoint.GetComponent<Collider>();
        if (npCollider != null)
            npCollider.enabled = false;

        if (thumbsUpPrefab != null)
        {
            Vector3 spawnPos = navPoint.transform.position + Vector3.up * thumbsUpYOffset;
            var thumbs = Instantiate(thumbsUpPrefab, spawnPos, Quaternion.identity);
            Destroy(thumbs, thumbsUpLifetime);
        }

        yield return new WaitForSeconds(specialCleanupDelay);

        hasInitialized = false;

        if (_finalInstructionsCo != null)
        {
            StopCoroutine(_finalInstructionsCo);
            _finalInstructionsCo = null;
        }

        _remindersEnabled = false;
        _currentReminderClip = null;
        _currentReminderVolume = 0f;
        if (_reminderLoopCo != null)
        {
            StopCoroutine(_reminderLoopCo);
            _reminderLoopCo = null;
        }
        if (!_priorityActive && _audioSource != null)
            _audioSource.Stop();

        if (pathManager != null)
        {
            pathManager.string_start = "";
            pathManager.game_start = false;
        }

        if (_loadedAnchors != null && _loadedAnchors.Count > 0)
            SetAnchorVisualsEnabled(_loadedAnchors, true);

        ClearExistingNavPoints();

        if (SpatialAnchorManager.Instance != null)
            SpatialAnchorManager.Instance.ClearOnlyAnchorPrefabs();

        _useFinalReminder = false;
        _lastReminderTime = 0f;
    }

    void OnTriggerExit(Collider other)
    {
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

    private IEnumerator ReminderLoop()
    {
        while (hasInitialized && pathManager != null && pathManager.game_start && _remindersEnabled)
        {
            float sinceLastInteraction = Time.time - Mathf.Max(_lastTouchTime, _lastReminderTime);
            if (sinceLastInteraction >= reminderInactivitySeconds)
                TryPlayReminder();
            yield return null;
        }
    }

    private void TryPlayReminder()
    {
        if (!_remindersEnabled)
            return;
        if (_audioSource == null)
            return;

        var clip = _currentReminderClip;
        var vol = _currentReminderVolume;

        if (clip == null)
            return;
        if (_priorityActive)
            return;
        if (_audioSource.isPlaying)
            return;

        _audioSource.PlayOneShot(clip, vol);
        _lastReminderTime = Time.time;
    }

    private void PlayPriorityClip(AudioClip clip, float volume)
    {
        if (clip == null || _audioSource == null)
            return;

        _audioSource.Stop();
        _priorityActive = true;
        if (_priorityResetCo != null) StopCoroutine(_priorityResetCo);
        _audioSource.PlayOneShot(clip, volume);
        _priorityResetCo = StartCoroutine(ResetPriorityAfter(clip.length));
    }

    private IEnumerator ResetPriorityAfter(float seconds)
    {
        if (seconds > 0f)
            yield return new WaitForSeconds(seconds);
        _priorityActive = false;
    }

    private IEnumerator PlayFinalInstructionsAfterDelay(float delay)
    {
        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        while (_priorityActive || (_audioSource != null && _audioSource.isPlaying))
            yield return null;

        PlayPriorityClip(finalInstructionsSound, finalInstructionsVolume);
    }

    private void SetAnchorVisualsEnabled(IEnumerable<OVRSpatialAnchor> anchors, bool enabled)
    {
        foreach (var a in anchors)
        {
            if (a == null) continue;
            foreach (var r in a.GetComponentsInChildren<Renderer>(true)) r.enabled = enabled;
            foreach (var lr in a.GetComponentsInChildren<LineRenderer>(true)) lr.enabled = enabled;
            foreach (var tr in a.GetComponentsInChildren<TrailRenderer>(true)) tr.enabled = enabled;
            foreach (var c in a.GetComponentsInChildren<Canvas>(true)) c.enabled = enabled;
        }
    }
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
//    [SerializeField] private Collider handCollider;

//    [Header("Audio")]
//    public AudioClip unmarkedHitSound;
//    [Range(0f, 1f)] public float unmarkedHitVolume = 1f;
//    public AudioClip specialHitSound;
//    [Range(0f, 1f)] public float specialHitVolume = 1f;
//    private AudioSource _audioSource;

//    [Header("Guidance / Instruction Audio")]
//    public AudioClip reminderSound;
//    [Range(0f, 1f)] public float reminderVolume = 1f;
//    public float reminderInactivitySeconds = 7f;
//    public AudioClip finalInstructionsSound;
//    [Range(0f, 1f)] public float finalInstructionsVolume = 1f;

//    [Header("Interaction Timing")]
//    public float initialInteractionDelay = 1.0f;
//    public float postRevealInteractionDelay = 0.25f;

//    [Header("SPECIAL Feedback")]
//    public GameObject thumbsUpPrefab;
//    public float thumbsUpYOffset = 0.2f;
//    public float thumbsUpLifetime = 2f;
//    public float specialCleanupDelay = 3f;

//    private List<GameObject> navPoints = new List<GameObject>();
//    private GameObject firstNavPoint;
//    private GameObject latestNavPoint;
//    private bool hasInitialized = false;

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

//    private readonly Dictionary<Renderer, Color> _originalColors = new();

//    private Coroutine _reminderLoopCo;
//    private Coroutine _priorityResetCo;
//    private float _lastTouchTime;
//    private float _lastReminderTime;
//    private bool _priorityActive = false;
//    private bool _finalInstructionsQueued = false;

//    private float _interactionUnlockTime = 0f;

//    private AudioClip _currentReminderClip;
//    private float _currentReminderVolume;
//    private bool _useFinalReminder = false;

//    void Awake()
//    {
//        if (handCollider == null)
//            handCollider = GetComponent<Collider>();

//        if (_audioSource == null)
//            _audioSource = GetComponent<AudioSource>();
//        if (_audioSource == null)
//            _audioSource = gameObject.AddComponent<AudioSource>();
//        _audioSource.playOnAwake = false;
//    }

//    void Update()
//    {
//        UpdateColliderState();

//        if (!hasInitialized && pathManager != null)
//        {
//            string startCode = pathManager.string_start;
//            bool started = pathManager.game_start;
//            bool isValidStart = !string.IsNullOrWhiteSpace(startCode);

//            if (started && isValidStart)
//            {
//                hasInitialized = true;
//                StartCoroutine(LoadNavPointsFromFile(startCode));
//            }
//        }
//    }

//    private void UpdateColliderState()
//    {
//        if (handCollider == null)
//            return;

//        bool anyButtonDown = false;

//        foreach (var btn in _buttonsToCheck)
//        {
//            if (OVRInput.Get(btn))
//            {
//                anyButtonDown = true;
//                break;
//            }
//        }

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
//        ClearExistingNavPoints();

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

//            if (i > 0)
//                go.SetActive(false);

//            navPoints.Add(go);
//            latestNavPoint = go;

//            if (i == 0)
//                firstNavPoint = go;
//        }

//        if (navPoints.Count > 0)
//        {
//            var last = navPoints[navPoints.Count - 1];
//            last.tag = "SPECIAL";
//            latestNavPoint = last;
//        }

//        _lastTouchTime = Time.time;
//        _lastReminderTime = -999f;
//        _finalInstructionsQueued = false;

//        _currentReminderClip = reminderSound;
//        _currentReminderVolume = reminderVolume;
//        _useFinalReminder = false;

//        _interactionUnlockTime = Time.time + Mathf.Max(0f, initialInteractionDelay);

//        if (_reminderLoopCo != null) StopCoroutine(_reminderLoopCo);
//        _reminderLoopCo = StartCoroutine(ReminderLoop());

//        yield return new WaitForSeconds(Mathf.Max(0f, initialInteractionDelay));
//        TryPlayReminder();

//        yield break;
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
//        if (Time.time < _interactionUnlockTime)
//            return;

//        if (hasInitialized && pathManager != null && pathManager.game_start)
//        {
//            GameObject specialNavPoint = null;
//            if (other.CompareTag("SPECIAL"))
//                specialNavPoint = other.gameObject;
//            else if (other.transform.parent != null && other.transform.parent.CompareTag("SPECIAL"))
//                specialNavPoint = other.transform.parent.gameObject;

//            if (specialNavPoint != null)
//            {
//                _lastTouchTime = Time.time;
//                StartCoroutine(HandleSpecialNavPointHit(specialNavPoint));
//                return;
//            }

//            GameObject navPoint = null;
//            if (other.CompareTag("UNMARKED"))
//                navPoint = other.gameObject;
//            else if (other.transform.parent != null && other.transform.parent.CompareTag("UNMARKED"))
//                navPoint = other.transform.parent.gameObject;

//            if (navPoint != null)
//            {
//                _lastTouchTime = Time.time;

//                if (unmarkedHitSound != null)
//                    PlayPriorityClip(unmarkedHitSound, unmarkedHitVolume);

//                var renderers = navPoint.GetComponentsInChildren<Renderer>();
//                foreach (var rend in renderers)
//                {
//                    if (!_originalColors.ContainsKey(rend))
//                        _originalColors[rend] = rend.material.color;
//                    rend.material.color = Color.white;
//                }

//                foreach (Transform child in navPoint.transform)
//                    child.gameObject.SetActive(false);

//                navPoint.tag = "UNMARKED";

//                var npCollider = navPoint.GetComponent<Collider>();
//                if (npCollider != null)
//                    npCollider.enabled = false;

//                int currentIndex = navPoints.IndexOf(navPoint);
//                if (currentIndex >= 0 && currentIndex + 1 < navPoints.Count)
//                {
//                    var next = navPoints[currentIndex + 1];
//                    next.SetActive(true);
//                    latestNavPoint = next;

//                    _interactionUnlockTime = Time.time + Mathf.Max(0f, postRevealInteractionDelay);

//                    if (!_finalInstructionsQueued && currentIndex == navPoints.Count - 2)
//                    {
//                        if (finalInstructionsSound != null)
//                        {
//                            _currentReminderClip = finalInstructionsSound;
//                            _currentReminderVolume = finalInstructionsVolume;
//                            _useFinalReminder = true;
//                            _lastReminderTime = Time.time;
//                        }

//                        float delay = (unmarkedHitSound != null ? unmarkedHitSound.length : 0f) + 1f;
//                        StartCoroutine(PlayFinalInstructionsAfterDelay(delay));
//                        _finalInstructionsQueued = true;
//                    }
//                }

//                return;
//            }
//        }

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

//    private IEnumerator HandleSpecialNavPointHit(GameObject navPoint)
//    {
//        if (specialHitSound != null)
//            PlayPriorityClip(specialHitSound, specialHitVolume);

//        var renderers = navPoint.GetComponentsInChildren<Renderer>();
//        foreach (var rend in renderers)
//        {
//            if (!_originalColors.ContainsKey(rend))
//                _originalColors[rend] = rend.material.color;
//            rend.material.color = Color.white;
//        }

//        foreach (Transform child in navPoint.transform)
//            child.gameObject.SetActive(false);

//        navPoint.tag = "SPECIAL";

//        var npCollider = navPoint.GetComponent<Collider>();
//        if (npCollider != null)
//            npCollider.enabled = false;

//        if (thumbsUpPrefab != null)
//        {
//            Vector3 spawnPos = navPoint.transform.position + Vector3.up * thumbsUpYOffset;
//            var thumbs = Instantiate(thumbsUpPrefab, spawnPos, Quaternion.identity);
//            Destroy(thumbs, thumbsUpLifetime);
//        }

//        yield return new WaitForSeconds(specialCleanupDelay);

//        hasInitialized = false;
//        if (_reminderLoopCo != null)
//        {
//            StopCoroutine(_reminderLoopCo);
//            _reminderLoopCo = null;
//        }
//        if (pathManager != null)
//        {
//            pathManager.string_start = "";
//            pathManager.game_start = false;
//        }

//        ClearExistingNavPoints();

//        if (SpatialAnchorManager.Instance != null)
//            SpatialAnchorManager.Instance.ClearOnlyAnchorPrefabs();

//        _useFinalReminder = false;
//        _currentReminderClip = reminderSound;
//        _currentReminderVolume = reminderVolume;
//    }

//    void OnTriggerExit(Collider other)
//    {
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

//    private IEnumerator ReminderLoop()
//    {
//        while (hasInitialized && pathManager != null && pathManager.game_start)
//        {
//            float sinceLastInteraction = Time.time - Mathf.Max(_lastTouchTime, _lastReminderTime);
//            if (sinceLastInteraction >= reminderInactivitySeconds)
//                TryPlayReminder();
//            yield return null;
//        }
//    }

//    private void TryPlayReminder()
//    {
//        if (_audioSource == null)
//            return;

//        var clip = _currentReminderClip;
//        var vol = _currentReminderVolume;

//        if (clip == null)
//            return;
//        if (_priorityActive)
//            return;
//        if (_audioSource.isPlaying)
//            return;

//        _audioSource.PlayOneShot(clip, vol);
//        _lastReminderTime = Time.time;
//    }

//    private void PlayPriorityClip(AudioClip clip, float volume)
//    {
//        if (clip == null || _audioSource == null)
//            return;

//        _audioSource.Stop();
//        _priorityActive = true;
//        if (_priorityResetCo != null) StopCoroutine(_priorityResetCo);
//        _audioSource.PlayOneShot(clip, volume);
//        _priorityResetCo = StartCoroutine(ResetPriorityAfter(clip.length));
//    }

//    private IEnumerator ResetPriorityAfter(float seconds)
//    {
//        if (seconds > 0f)
//            yield return new WaitForSeconds(seconds);
//        _priorityActive = false;
//    }

//    private IEnumerator PlayFinalInstructionsAfterDelay(float delay)
//    {
//        if (delay > 0f)
//            yield return new WaitForSeconds(delay);

//        while (_priorityActive || (_audioSource != null && _audioSource.isPlaying))
//            yield return null;

//        PlayPriorityClip(finalInstructionsSound, finalInstructionsVolume);
//    }
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

//    [Header("Collider Control")]
//    [SerializeField] private Collider handCollider; // can be assigned manually; falls back to GetComponent<Collider>()

//    [Header("Audio")]
//    public AudioClip unmarkedHitSound;
//    [Range(0f, 1f)] public float unmarkedHitVolume = 1f;
//    public AudioClip specialHitSound;
//    [Range(0f, 1f)] public float specialHitVolume = 1f;
//    private AudioSource _audioSource;

//    [Header("Guidance / Instruction Audio")]
//    public AudioClip reminderSound;
//    [Range(0f, 1f)] public float reminderVolume = 1f;
//    public float reminderInactivitySeconds = 7f;
//    public AudioClip finalInstructionsSound;
//    [Range(0f, 1f)] public float finalInstructionsVolume = 1f;

//    [Header("SPECIAL Feedback")]
//    public GameObject thumbsUpPrefab;
//    public float thumbsUpYOffset = 0.2f;
//    public float thumbsUpLifetime = 2f;
//    public float specialCleanupDelay = 3f;

//    private List<GameObject> navPoints = new List<GameObject>();
//    private GameObject firstNavPoint;
//    private GameObject latestNavPoint;
//    private bool hasInitialized = false;

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

//    private readonly Dictionary<Renderer, Color> _originalColors = new();

//    private Coroutine _reminderLoopCo;
//    private Coroutine _priorityResetCo;
//    private float _lastTouchTime;
//    private float _lastReminderTime;
//    private bool _priorityActive = false;
//    private bool _finalInstructionsQueued = false;

//    void Awake()
//    {
//        if (handCollider == null)
//            handCollider = GetComponent<Collider>();

//        if (handCollider == null)
//            Debug.LogWarning("[GameManager] No collider found on this GameObject or assigned to handCollider.");

//        _audioSource = GetComponent<AudioSource>();
//        if (_audioSource == null)
//            _audioSource = gameObject.AddComponent<AudioSource>();
//        _audioSource.playOnAwake = false;
//    }

//    void Update()
//    {
//        UpdateColliderState();

//        if (!hasInitialized && pathManager != null)
//        {
//            string startCode = pathManager.string_start;
//            bool started = pathManager.game_start;
//            bool isValidStart = !string.IsNullOrWhiteSpace(startCode);

//            if (started && isValidStart)
//            {
//                hasInitialized = true;
//                StartCoroutine(LoadNavPointsFromFile(startCode));
//            }
//        }
//    }

//    private void UpdateColliderState()
//    {
//        if (handCollider == null)
//            return;

//        bool anyButtonDown = false;

//        foreach (var btn in _buttonsToCheck)
//        {
//            if (OVRInput.Get(btn))
//            {
//                anyButtonDown = true;
//                break;
//            }
//        }

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
//        ClearExistingNavPoints();

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

//            if (i > 0)
//                go.SetActive(false);

//            navPoints.Add(go);
//            latestNavPoint = go;

//            if (i == 0)
//                firstNavPoint = go;
//        }

//        if (navPoints.Count > 0)
//        {
//            var last = navPoints[navPoints.Count - 1];
//            last.tag = "SPECIAL";
//            latestNavPoint = last;
//        }

//        _lastTouchTime = Time.time;
//        _lastReminderTime = -999f;
//        _finalInstructionsQueued = false;

//        if (_reminderLoopCo != null) StopCoroutine(_reminderLoopCo);
//        _reminderLoopCo = StartCoroutine(ReminderLoop());

//        yield return new WaitForSeconds(1f);
//        TryPlayReminder();

//        yield break;
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
//        if (hasInitialized && pathManager != null && pathManager.game_start)
//        {
//            GameObject specialNavPoint = null;
//            if (other.CompareTag("SPECIAL"))
//                specialNavPoint = other.gameObject;
//            else if (other.transform.parent != null && other.transform.parent.CompareTag("SPECIAL"))
//                specialNavPoint = other.transform.parent.gameObject;

//            if (specialNavPoint != null)
//            {
//                _lastTouchTime = Time.time;
//                StartCoroutine(HandleSpecialNavPointHit(specialNavPoint));
//                return;
//            }

//            GameObject navPoint = null;
//            if (other.CompareTag("UNMARKED"))
//                navPoint = other.gameObject;
//            else if (other.transform.parent != null && other.transform.parent.CompareTag("UNMARKED"))
//                navPoint = other.transform.parent.gameObject;

//            if (navPoint != null)
//            {
//                _lastTouchTime = Time.time;

//                if (unmarkedHitSound != null)
//                    PlayPriorityClip(unmarkedHitSound, unmarkedHitVolume);
//                else
//                    Debug.LogWarning("[GameManager] UNMARKED hit sound clip not assigned.");

//                var renderers = navPoint.GetComponentsInChildren<Renderer>();
//                foreach (var rend in renderers)
//                {
//                    if (!_originalColors.ContainsKey(rend))
//                        _originalColors[rend] = rend.material.color;
//                    rend.material.color = Color.white;
//                }

//                foreach (Transform child in navPoint.transform)
//                    child.gameObject.SetActive(false);

//                navPoint.tag = "UNMARKED";

//                var npCollider = navPoint.GetComponent<Collider>();
//                if (npCollider != null)
//                    npCollider.enabled = false;

//                int currentIndex = navPoints.IndexOf(navPoint);
//                if (currentIndex >= 0 && currentIndex + 1 < navPoints.Count)
//                {
//                    var next = navPoints[currentIndex + 1];
//                    next.SetActive(true);
//                    latestNavPoint = next;

//                    if (!_finalInstructionsQueued && currentIndex == navPoints.Count - 2)
//                    {
//                        float delay = (unmarkedHitSound != null ? unmarkedHitSound.length : 0f) + 1f;
//                        StartCoroutine(PlayFinalInstructionsAfterDelay(delay));
//                        _finalInstructionsQueued = true;
//                    }
//                }

//                return;
//            }
//        }

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

//    private IEnumerator HandleSpecialNavPointHit(GameObject navPoint)
//    {
//        if (specialHitSound != null)
//            PlayPriorityClip(specialHitSound, specialHitVolume);
//        else
//            Debug.LogWarning("[GameManager] SPECIAL hit sound clip not assigned.");

//        var renderers = navPoint.GetComponentsInChildren<Renderer>();
//        foreach (var rend in renderers)
//        {
//            if (!_originalColors.ContainsKey(rend))
//                _originalColors[rend] = rend.material.color;
//            rend.material.color = Color.white;
//        }

//        foreach (Transform child in navPoint.transform)
//            child.gameObject.SetActive(false);

//        navPoint.tag = "SPECIAL";

//        var npCollider = navPoint.GetComponent<Collider>();
//        if (npCollider != null)
//            npCollider.enabled = false;

//        if (thumbsUpPrefab != null)
//        {
//            Vector3 spawnPos = navPoint.transform.position + Vector3.up * thumbsUpYOffset;
//            var thumbs = Instantiate(thumbsUpPrefab, spawnPos, Quaternion.identity);
//            Destroy(thumbs, thumbsUpLifetime);
//        }
//        else
//        {
//            Debug.LogWarning("[GameManager] thumbsUpPrefab not assigned.");
//        }

//        yield return new WaitForSeconds(specialCleanupDelay);

//        hasInitialized = false;
//        if (_reminderLoopCo != null)
//        {
//            StopCoroutine(_reminderLoopCo);
//            _reminderLoopCo = null;
//        }
//        if (pathManager != null)
//        {
//            pathManager.string_start = "";
//            pathManager.game_start = false;
//        }

//        ClearExistingNavPoints();

//        if (SpatialAnchorManager.Instance != null)
//            SpatialAnchorManager.Instance.ClearOnlyAnchorPrefabs();
//    }

//    void OnTriggerExit(Collider other)
//    {
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

//    private IEnumerator ReminderLoop()
//    {
//        while (hasInitialized && pathManager != null && pathManager.game_start)
//        {
//            float sinceLastInteraction = Time.time - Mathf.Max(_lastTouchTime, _lastReminderTime);
//            if (sinceLastInteraction >= reminderInactivitySeconds)
//                TryPlayReminder();
//            yield return null;
//        }
//    }

//    private void TryPlayReminder()
//    {
//        if (reminderSound == null || _audioSource == null)
//            return;
//        if (_priorityActive)
//            return;
//        if (_audioSource.isPlaying)
//            return;

//        _audioSource.PlayOneShot(reminderSound, reminderVolume);
//        _lastReminderTime = Time.time;
//    }

//    private void PlayPriorityClip(AudioClip clip, float volume)
//    {
//        if (clip == null || _audioSource == null)
//            return;

//        _audioSource.Stop();
//        _priorityActive = true;
//        if (_priorityResetCo != null) StopCoroutine(_priorityResetCo);
//        _audioSource.PlayOneShot(clip, volume);
//        _priorityResetCo = StartCoroutine(ResetPriorityAfter(clip.length));
//    }

//    private IEnumerator ResetPriorityAfter(float seconds)
//    {
//        if (seconds > 0f)
//            yield return new WaitForSeconds(seconds);
//        _priorityActive = false;
//    }

//    private IEnumerator PlayFinalInstructionsAfterDelay(float delay)
//    {
//        if (delay > 0f)
//            yield return new WaitForSeconds(delay);

//        while (_priorityActive || (_audioSource != null && _audioSource.isPlaying))
//            yield return null;

//        PlayPriorityClip(finalInstructionsSound, finalInstructionsVolume);
//    }
//}

