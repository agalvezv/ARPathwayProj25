using UnityEngine;
using PassthroughCameraSamples;



public class CameraManager : MonoBehaviour
{

    public WebCamTextureManager webcamManager;
    public Renderer quadRenderer;



    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {
        if (webcamManager.WebCamTexture != null)
        {
            quadRenderer.material.mainTexture = webcamManager.WebCamTexture;
        }
    }
}
