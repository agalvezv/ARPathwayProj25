using PassthroughCameraSamples;
using UnityEngine;

public class DisplayManagerV2 : MonoBehaviour
{
    public WebCamTextureManager webcamManager;
    public Renderer quadRenderer;

    public string textureName;

    private Texture2D snapFrame;

    public float quadDistance;



    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        quadRenderer.gameObject.SetActive(false);
    }

    // Update is called once per frame
    void Update()
    {
        if (webcamManager.WebCamTexture != null)
        {
            if(OVRInput.GetDown(OVRInput.Button.One))
            {
                TakePicture();
                PlaceQuad();
            }

        }
    }

    public void TakePicture()
    {


        quadRenderer.gameObject.SetActive(true);
        int width = webcamManager.WebCamTexture.width;
        int height = webcamManager.WebCamTexture.height;

        if (snapFrame == null)
        {
            snapFrame = new Texture2D(width, height);

        }

        Color32[] pixels = new Color32[width * height];
        webcamManager.WebCamTexture.GetPixels32(pixels);


        snapFrame.SetPixels32(pixels);
        snapFrame.Apply();

        //quadRenderer.material.mainTexture = snapFrame;
        quadRenderer.material.SetTexture(textureName, snapFrame);
    }

    public void PlaceQuad()
    {
        Transform quadTransform = quadRenderer.transform;

        Pose cameraPose = PassthroughCameraUtils.GetCameraPoseInWorld(PassthroughCameraEye.Left);

        Vector2Int resolution = PassthroughCameraUtils.GetCameraIntrinsics(PassthroughCameraEye.Left).Resolution;

        quadTransform.position = cameraPose.position + cameraPose.forward * quadDistance;
        quadTransform.rotation = cameraPose.rotation;

        Ray leftSide = PassthroughCameraUtils.ScreenPointToRayInCamera(PassthroughCameraEye.Left, new Vector2Int(0, resolution.y / 2));
        Ray rightSide = PassthroughCameraUtils.ScreenPointToRayInCamera(PassthroughCameraEye.Left, new Vector2Int(resolution.x, resolution.y / 2));

        float horizontalFov = Vector3.Angle(leftSide.direction, rightSide.direction);

        float quadScale = 2 * quadDistance * Mathf.Tan(horizontalFov * Mathf.Deg2Rad / 2);


        float ratio = (float)snapFrame.height / (float)snapFrame.width;

        quadTransform.localScale = new Vector3(quadScale, ratio, 1);
    }
}
