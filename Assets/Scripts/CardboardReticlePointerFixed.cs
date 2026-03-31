// Patched version of CardboardReticlePointer that suppresses
// "SendMessage has no receiver!" warnings on non-interactive objects.
// Replace the original CardboardReticlePointer component with this one on your Player/Camera.

using UnityEngine;

public class CardboardReticlePointerFixed : MonoBehaviour
{
    [Range(-32767, 32767)]
    public int ReticleSortingOrder = 32767;

    public LayerMask ReticleInteractionLayerMask = 1 << _RETICLE_INTERACTION_DEFAULT_LAYER;

    private const int _RETICLE_INTERACTION_DEFAULT_LAYER = 8;
    private const float _RETICLE_MIN_INNER_ANGLE = 0.0f;
    private const float _RETICLE_MIN_OUTER_ANGLE = 0.5f;
    private const float _RETICLE_GROWTH_ANGLE = 1.5f;
    private const float _RETICLE_MIN_DISTANCE = 0.45f;
    private const float _RETICLE_MAX_DISTANCE = 20.0f;
    private const int _RETICLE_SEGMENTS = 20;
    private const float _RETICLE_GROWTH_SPEED = 8.0f;

    private GameObject _gazedAtObject = null;
    private Material _reticleMaterial;
    private float _reticleInnerAngle;
    private float _reticleOuterAngle;
    private float _reticleDistanceInMeters;
    private float _reticleInnerDiameter;
    private float _reticleOuterDiameter;

    private void Start()
    {
        Renderer rendererComponent = GetComponent<Renderer>();
        rendererComponent.sortingOrder = ReticleSortingOrder;
        _reticleMaterial = rendererComponent.material;
        CreateMesh();
    }

    private void Update()
    {
        RaycastHit hit;
        if (Physics.Raycast(transform.position, transform.forward, out hit, _RETICLE_MAX_DISTANCE))
        {
            if (_gazedAtObject != hit.transform.gameObject)
            {
                // FIX: Added SendMessageOptions.DontRequireReceiver to prevent warnings
                _gazedAtObject?.SendMessage("OnPointerExit", SendMessageOptions.DontRequireReceiver);
                _gazedAtObject = hit.transform.gameObject;
                _gazedAtObject.SendMessage("OnPointerEnter", SendMessageOptions.DontRequireReceiver);
            }

            bool isInteractive = (1 << _gazedAtObject.layer & ReticleInteractionLayerMask) != 0;
            SetParams(hit.distance, isInteractive);
        }
        else
        {
            // FIX: Added SendMessageOptions.DontRequireReceiver to prevent warnings
            _gazedAtObject?.SendMessage("OnPointerExit", SendMessageOptions.DontRequireReceiver);
            _gazedAtObject = null;
            ResetParams();
        }

        if (Google.XR.Cardboard.Api.IsTriggerPressed)
        {
            _gazedAtObject?.SendMessage("OnPointerClick", SendMessageOptions.DontRequireReceiver);
        }

        UpdateDiameters();
    }

    private void UpdateDiameters()
    {
        _reticleDistanceInMeters =
            Mathf.Clamp(_reticleDistanceInMeters, _RETICLE_MIN_DISTANCE, _RETICLE_MAX_DISTANCE);

        if (_reticleInnerAngle < _RETICLE_MIN_INNER_ANGLE)
            _reticleInnerAngle = _RETICLE_MIN_INNER_ANGLE;

        if (_reticleOuterAngle < _RETICLE_MIN_OUTER_ANGLE)
            _reticleOuterAngle = _RETICLE_MIN_OUTER_ANGLE;

        float inner_half_angle_radians = Mathf.Deg2Rad * _reticleInnerAngle * 0.5f;
        float outer_half_angle_radians = Mathf.Deg2Rad * _reticleOuterAngle * 0.5f;

        float inner_diameter = 2.0f * Mathf.Tan(inner_half_angle_radians);
        float outer_diameter = 2.0f * Mathf.Tan(outer_half_angle_radians);

        _reticleInnerDiameter = Mathf.Lerp(
            _reticleInnerDiameter, inner_diameter, Time.unscaledDeltaTime * _RETICLE_GROWTH_SPEED);
        _reticleOuterDiameter = Mathf.Lerp(
            _reticleOuterDiameter, outer_diameter, Time.unscaledDeltaTime * _RETICLE_GROWTH_SPEED);

        _reticleMaterial.SetFloat("_InnerDiameter", _reticleInnerDiameter * _reticleDistanceInMeters);
        _reticleMaterial.SetFloat("_OuterDiameter", _reticleOuterDiameter * _reticleDistanceInMeters);
        _reticleMaterial.SetFloat("_DistanceInMeters", _reticleDistanceInMeters);
    }

    private void SetParams(float distance, bool interactive)
    {
        _reticleDistanceInMeters = Mathf.Clamp(distance, _RETICLE_MIN_DISTANCE, _RETICLE_MAX_DISTANCE);
        if (interactive)
        {
            _reticleInnerAngle = _RETICLE_MIN_INNER_ANGLE + _RETICLE_GROWTH_ANGLE;
            _reticleOuterAngle = _RETICLE_MIN_OUTER_ANGLE + _RETICLE_GROWTH_ANGLE;
        }
        else
        {
            _reticleInnerAngle = _RETICLE_MIN_INNER_ANGLE;
            _reticleOuterAngle = _RETICLE_MIN_OUTER_ANGLE;
        }
    }

    private void ResetParams()
    {
        _reticleDistanceInMeters = _RETICLE_MAX_DISTANCE;
        _reticleInnerAngle = _RETICLE_MIN_INNER_ANGLE;
        _reticleOuterAngle = _RETICLE_MIN_OUTER_ANGLE;
    }

    private void CreateMesh()
    {
        Mesh mesh = new Mesh();
        gameObject.AddComponent<MeshFilter>();
        GetComponent<MeshFilter>().mesh = mesh;

        int segments_count = _RETICLE_SEGMENTS;
        int vertex_count = (segments_count + 1) * 2;

        Vector3[] vertices = new Vector3[vertex_count];

        const float kTwoPi = Mathf.PI * 2.0f;
        int vi = 0;
        for (int si = 0; si <= segments_count; ++si)
        {
            float angle = (float)si / (float)segments_count * kTwoPi;
            float x = Mathf.Sin(angle);
            float y = Mathf.Cos(angle);
            vertices[vi++] = new Vector3(x, y, 0.0f);
            vertices[vi++] = new Vector3(x, y, 1.0f);
        }

        int indices_count = (segments_count + 1) * 3 * 2;
        int[] indices = new int[indices_count];

        int vert = 0;
        int idx = 0;
        for (int si = 0; si < segments_count; ++si)
        {
            indices[idx++] = vert + 1;
            indices[idx++] = vert;
            indices[idx++] = vert + 2;

            indices[idx++] = vert + 1;
            indices[idx++] = vert + 2;
            indices[idx++] = vert + 3;

            vert += 2;
        }

        mesh.vertices = vertices;
        mesh.triangles = indices;
        mesh.RecalculateBounds();
    }
}
