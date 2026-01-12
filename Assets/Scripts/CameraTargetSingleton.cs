using UnityEngine;

public class CameraTargetSingleton : MonoBehaviour
{
    public static CameraTargetSingleton Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("Multiple instances of CameraTargetSingleton detected. Destroying duplicate.");
            Destroy(this.gameObject);
            return;
        }

        Instance = this;
    }
}
