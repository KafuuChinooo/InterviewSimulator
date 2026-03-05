using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SpherePanoramaSwitcher : MonoBehaviour
{
    public Texture2D[] textures;                 
    public string textureProperty = "_MainTex";  

    private int currentIndex = 0;
    private Renderer sphereRenderer;

    void Start()
    {
        sphereRenderer = GetComponent<Renderer>();

        if (textures.Length > 0)
        {
            ApplyTexture(currentIndex);
        }
    }

    public void NextTexture()
    {
        currentIndex = (currentIndex + 1) % textures.Length;
        ApplyTexture(currentIndex);
    }

    public void PreviousTexture()
    {
        currentIndex = (currentIndex - 1 + textures.Length) % textures.Length;
        ApplyTexture(currentIndex);
    }

    private void ApplyTexture(int index)
    {
        if (sphereRenderer != null && textures[index] != null)
        {
            sphereRenderer.material.SetTexture(textureProperty, textures[index]);
        }
    }
}
