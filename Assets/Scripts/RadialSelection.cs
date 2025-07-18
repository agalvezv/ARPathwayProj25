using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using TMPro;

public class RadialSelection : MonoBehaviour
{
    [Header("Input")]
    public OVRInput.Button spawnButton;

    [Header("Radial Configuration")]
    [Range(2, 10)]
    public int numberOfRadialPart = 6;
    [Tooltip("Must match Number Of Radial Parts")]
    public List<string> partNames = new List<string>();

    [Header("References")]
    public GameObject radialPartPrefab;
    public Transform radialPartCanvas;
    public float angleBetweenPart = 10f;
    public Transform handTransform;

    [Header("Events")]
    public UnityEvent<int> OnPartSelected;

    private List<GameObject> spawnedParts = new List<GameObject>();
    private int currentSelectedRadialPart = -1;

    void OnValidate()
    {
        if (partNames == null) partNames = new List<string>();
        while (partNames.Count < numberOfRadialPart)
            partNames.Add($"Part {partNames.Count + 1}");
        while (partNames.Count > numberOfRadialPart)
            partNames.RemoveAt(partNames.Count - 1);
    }

    void Update()
    {
        if (OVRInput.GetDown(spawnButton))
            SpawnRadialPart();

        if (OVRInput.Get(spawnButton))
            GetSelectedRadialPart();

        if (OVRInput.GetUp(spawnButton))
            HideAndTriggerSelected();
    }

    public void HideAndTriggerSelected()
    {
        OnPartSelected.Invoke(currentSelectedRadialPart);
        radialPartCanvas.gameObject.SetActive(false);
    }

    public void GetSelectedRadialPart()
    {
        Vector3 dir = handTransform.position - radialPartCanvas.position;
        Vector3 projected = Vector3.ProjectOnPlane(dir, radialPartCanvas.forward);
        float angle = Vector3.SignedAngle(radialPartCanvas.up, projected, -radialPartCanvas.forward);
        if (angle < 0) angle += 360f;

        currentSelectedRadialPart = Mathf.FloorToInt(angle * numberOfRadialPart / 360f);

        for (int i = 0; i < spawnedParts.Count; i++)
        {
            var img = spawnedParts[i].GetComponent<Image>();
            if (i == currentSelectedRadialPart)
            {
                img.color = Color.yellow;
                spawnedParts[i].transform.localScale = Vector3.one * 1.1f;
            }
            else
            {
                img.color = Color.white;
                spawnedParts[i].transform.localScale = Vector3.one;
            }
        }
    }

    public void SpawnRadialPart()
    {
        radialPartCanvas.gameObject.SetActive(true);
        radialPartCanvas.position = handTransform.position;
        radialPartCanvas.rotation = handTransform.rotation;

        foreach (var go in spawnedParts)
            Destroy(go);
        spawnedParts.Clear();

        for (int i = 0; i < numberOfRadialPart; i++)
        {
            float zRot = -i * 360f / numberOfRadialPart - angleBetweenPart / 2f;

            var go = Instantiate(radialPartPrefab, radialPartCanvas);
            go.transform.localEulerAngles = new Vector3(0, 0, zRot);

            var img = go.GetComponent<Image>();
            img.fillAmount = 1f / numberOfRadialPart - (angleBetweenPart / 360f);

            var tmp = go.GetComponentInChildren<TMP_Text>();
            if (tmp != null)
                tmp.text = partNames[i];
            else
                Debug.LogWarning("RadialPartPrefab is missing a child TMP_Text component!");

            spawnedParts.Add(go);
        }
    }
}
