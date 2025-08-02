using PassthroughCameraSamples;
using System.Threading.Tasks;
using TMPro;
using System;
using System.Threading.Tasks;
using PassthroughCameraSamples;
using TMPro;
using UnityEngine;
using ZXing;
using ZXing.Common;

public class QRPhotoScanner : MonoBehaviour
{
    [Header("Passthrough")]
    public WebCamTextureManager webcamManager;

    [Header("Scan Settings")]
    [Tooltip("When true, the scanner will automatically attempt to decode at intervals")]
    public bool scanningEnabled = false;
    [Tooltip("Minimum seconds between scan attempts")]
    public float throttleDelay = 0.5f;

    [Header("UI")]
    [Tooltip("String to display the decoded QR or error code")]
    public string resultText;
    [Tooltip("TMP_Text to display the decoded QR code")]
    public TMP_Text resultText2;

    // Fired when a valid QR code (not “No QR found”) is decoded
    public event Action<string> OnQRScanned;

    // internal state
    private float _nextScanTime = 0f;
    private bool _isDecoding = false;
    private string _pendingResult = null;

    void Update()
    {
        // 1) Kick off a scan if scanningEnabled, passthrough ready, interval elapsed, and no decode in flight
        if (scanningEnabled
            && webcamManager.WebCamTexture != null
            && Time.time >= _nextScanTime
            && !_isDecoding)
        {
            _nextScanTime = Time.time + throttleDelay;
            StartDecode();
        }

        // 2) If a background result arrived, apply it
        if (_pendingResult != null)
        {
            resultText = _pendingResult;
            if (resultText2 != null)
                resultText2.text = resultText;

            // If it’s a “real” code, stop scanning and notify
            if (_pendingResult != "No QR found")
            {
                scanningEnabled = false;
                OnQRScanned?.Invoke(_pendingResult);
            }

            _pendingResult = null;
            _isDecoding = false;
        }
    }

    void StartDecode()
    {
        _isDecoding = true;

        // Capture pixels on the main thread
        var tex = webcamManager.WebCamTexture;
        int w = tex.width, h = tex.height;
        var pixels = tex.GetPixels32();

        // Offload ZXing decode to a background task
        Task.Run(() =>
        {
            var reader = new BarcodeReader
            {
                AutoRotate = true,
                Options = new DecodingOptions
                {
                    TryHarder = true,
                    PossibleFormats = new[] { BarcodeFormat.QR_CODE }
                }
            };

            var result = reader.Decode(pixels, w, h);
            _pendingResult = (result != null) ? result.Text : "No QR found";
        });
    }
}












//using PassthroughCameraSamples;
//using UnityEngine;
//using ZXing;                        // make sure this resolves
//using ZXing.Common;
//using TMPro;                        // for TextMeshProUGUI

//public class QRPhotoScanner : MonoBehaviour
//{
//    [Header("Passthrough")]
//    public WebCamTextureManager webcamManager;
//    public Renderer quadRenderer;
//    public string textureName = "_MainTex";

//    [Header("Scan Settings")]
//    [Tooltip("Which OVR button will trigger the scan")]
//    public OVRInput.Button scanButton = OVRInput.Button.Two;

//    [Header("UI")]
//    [Tooltip("TMP Text to display the decoded QR or error code")]
//    public string resultText;

//    Texture2D picture;
//    IBarcodeReader barcodeReader;

//    void Start()
//    {
//        // hide until snapshot
//        quadRenderer.gameObject.SetActive(false);

//        // configure ZXing to look for QR codes
//        var options = new DecodingOptions
//        {
//            TryHarder = true,
//            PossibleFormats = new[] { BarcodeFormat.QR_CODE }
//        };
//        barcodeReader = new BarcodeReader
//        {
//            AutoRotate = true,
//            Options = options
//        };

//        // clear any old text
//        if (resultText != null)
//            resultText = "";
//    }

//    void Update()
//    {
//        if (webcamManager.WebCamTexture != null &&
//            OVRInput.GetDown(scanButton))
//        {
//            TakePictureAndScan();
//        }
//    }

//    void TakePictureAndScan()
//    {
//        int w = webcamManager.WebCamTexture.width;
//        int h = webcamManager.WebCamTexture.height;

//        // (re)create our Texture2D if needed
//        if (picture == null || picture.width != w || picture.height != h)
//            picture = new Texture2D(w, h, TextureFormat.RGBA32, false);

//        // grab pixels from the passthrough camera
//        var pixels = webcamManager.WebCamTexture.GetPixels32();
//        picture.SetPixels32(pixels);
//        picture.Apply();

//        // show it on the quad
//        quadRenderer.material.SetTexture(textureName, picture);
//        quadRenderer.gameObject.SetActive(true);

//        // decode inside a try/catch so any error falls back to your error code
//        string displayText = "12345678";
//        try
//        {
//            var result = barcodeReader.Decode(pixels, w, h);
//            if (result != null)
//            {
//                Debug.Log($"QR Code detected: {result.Text}");
//                displayText = result.Text;
//            }
//            else
//            {
//                Debug.Log("No QR code found in the snapshot.");
//            }
//        }
//        catch (System.Exception ex)
//        {
//            Debug.LogError($"Error decoding QR: {ex.Message}");
//        }

//        // update TMP text
//        if (resultText != null)
//            resultText = displayText;

//        // hide the quad again now that scanning is done
//        quadRenderer.gameObject.SetActive(false);
//    }
//}
