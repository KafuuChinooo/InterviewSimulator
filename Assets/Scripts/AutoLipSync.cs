using UnityEngine;

[DisallowMultipleComponent]
public class AutoLipSync : MonoBehaviour
{
    [Header("References")]
    public AudioSource audioSource;
    public SkinnedMeshRenderer skinnedMeshRenderer;

    [Header("Blend Shape")]
    public string blendShapeName = "HRR";
    public float maxWeight = 100f;

    [Header("Tuning")]
    public float sensitivity = 1800f;
    public float silenceThreshold = 0.0025f;
    public float attackSpeed = 16f;
    public float releaseSpeed = 10f;

    private readonly float[] _samples = new float[128];
    private int _blendShapeIndex = -1;
    private float _currentWeight = 0f;

    private void Awake()
    {
        EnsureReferences();
        ResolveBlendShapeIndex();
    }

    private void OnEnable()
    {
        EnsureReferences();
        ResolveBlendShapeIndex();
    }

    private void OnValidate()
    {
        ResolveBlendShapeIndex();
    }

    private void OnDisable()
    {
        SetBlendWeight(0f);
    }

    private void LateUpdate()
    {
        EnsureReferences();
        if (skinnedMeshRenderer == null || _blendShapeIndex < 0)
        {
            return;
        }

        float targetWeight = 0f;
        if (audioSource != null && audioSource.isPlaying)
        {
            audioSource.GetOutputData(_samples, 0);
            float rms = CalculateRmsVolume();
            targetWeight = Mathf.Clamp((Mathf.Max(0f, rms - silenceThreshold)) * sensitivity, 0f, maxWeight);
        }

        float speed = targetWeight > _currentWeight ? attackSpeed : releaseSpeed;
        float t = 1f - Mathf.Exp(-speed * Time.deltaTime);
        _currentWeight = Mathf.Lerp(_currentWeight, targetWeight, t);
        SetBlendWeight(_currentWeight);
    }

    private void EnsureReferences()
    {
        if (skinnedMeshRenderer == null)
        {
            skinnedMeshRenderer = GetComponent<SkinnedMeshRenderer>();
        }

        if (audioSource == null)
        {
            AIAudioClient client = AIAudioClient.FindPreferredInstance();
            if (client != null)
            {
                audioSource = client.audioSource;
            }
        }
    }

    private void ResolveBlendShapeIndex()
    {
        if (skinnedMeshRenderer == null || skinnedMeshRenderer.sharedMesh == null)
        {
            _blendShapeIndex = -1;
            return;
        }

        Mesh mesh = skinnedMeshRenderer.sharedMesh;
        if (mesh.blendShapeCount == 0)
        {
            _blendShapeIndex = -1;
            return;
        }

        if (!string.IsNullOrWhiteSpace(blendShapeName))
        {
            _blendShapeIndex = mesh.GetBlendShapeIndex(blendShapeName);
        }

        if (_blendShapeIndex < 0 && mesh.blendShapeCount == 1)
        {
            _blendShapeIndex = 0;
            blendShapeName = mesh.GetBlendShapeName(0);
        }

        if (_blendShapeIndex < 0)
        {
            for (int i = 0; i < mesh.blendShapeCount; i++)
            {
                string candidate = mesh.GetBlendShapeName(i);
                string lowerCandidate = candidate.ToLowerInvariant();
                if (lowerCandidate.Contains("mouth") || lowerCandidate.Contains("open") || lowerCandidate.Contains("jaw"))
                {
                    _blendShapeIndex = i;
                    blendShapeName = candidate;
                    break;
                }
            }
        }
    }

    private float CalculateRmsVolume()
    {
        float sum = 0f;
        for (int i = 0; i < _samples.Length; i++)
        {
            float sample = _samples[i];
            sum += sample * sample;
        }

        return Mathf.Sqrt(sum / _samples.Length);
    }

    private void SetBlendWeight(float weight)
    {
        if (skinnedMeshRenderer == null || _blendShapeIndex < 0)
        {
            return;
        }

        skinnedMeshRenderer.SetBlendShapeWeight(_blendShapeIndex, Mathf.Clamp(weight, 0f, maxWeight));
    }
}
