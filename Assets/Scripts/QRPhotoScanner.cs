
using PassthroughCameraSamples;
using UnityEngine;
using ZXing;                        // make sure this resolves
using ZXing.Common;
using TMPro;                        // for TextMeshProUGUI

public class QRPhotoScanner : MonoBehaviour
{
    [Header("Passthrough")]
    public WebCamTextureManager webcamManager;
    public Renderer quadRenderer;
    public string textureName = "_MainTex";

    [Header("Scan Settings")]
    [Tooltip("Which OVR button will trigger the scan")]
    public OVRInput.Button scanButton = OVRInput.Button.Two;

    [Header("UI")]
    [Tooltip("TMP Text to display the decoded QR or error code")]
    public string resultText;

    Texture2D picture;
    IBarcodeReader barcodeReader;

    void Start()
    {
        // hide until snapshot
        quadRenderer.gameObject.SetActive(false);

        // configure ZXing to look for QR codes
        var options = new DecodingOptions
        {
            TryHarder = true,
            PossibleFormats = new[] { BarcodeFormat.QR_CODE }
        };
        barcodeReader = new BarcodeReader
        {
            AutoRotate = true,
            Options = options
        };

        // clear any old text
        if (resultText != null)
            resultText = "";
    }

    void Update()
    {
        if (webcamManager.WebCamTexture != null &&
            OVRInput.GetDown(scanButton))
        {
            TakePictureAndScan();
        }
    }

    void TakePictureAndScan()
    {
        int w = webcamManager.WebCamTexture.width;
        int h = webcamManager.WebCamTexture.height;

        // (re)create our Texture2D if needed
        if (picture == null || picture.width != w || picture.height != h)
            picture = new Texture2D(w, h, TextureFormat.RGBA32, false);

        // grab pixels from the passthrough camera
        var pixels = webcamManager.WebCamTexture.GetPixels32();
        picture.SetPixels32(pixels);
        picture.Apply();

        // show it on the quad
        quadRenderer.material.SetTexture(textureName, picture);
        quadRenderer.gameObject.SetActive(true);

        // decode inside a try/catch so any error falls back to your error code
        string displayText = "12345678";
        try
        {
            var result = barcodeReader.Decode(pixels, w, h);
            if (result != null)
            {
                Debug.Log($"QR Code detected: {result.Text}");
                displayText = result.Text;
            }
            else
            {
                Debug.Log("No QR code found in the snapshot.");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"Error decoding QR: {ex.Message}");
        }

        // update TMP text
        if (resultText != null)
            resultText = displayText;

        // hide the quad again now that scanning is done
        quadRenderer.gameObject.SetActive(false);
    }
}
