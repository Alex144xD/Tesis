using UnityEngine;

public class UIAllCanvasesDumper : MonoBehaviour
{
    [ContextMenu("Dump All Canvases")]
    public void Dump()
    {
        var canvases = FindObjectsOfType<Canvas>(true);
        Debug.Log($"[DUMPER] Canvases encontrados: {canvases.Length}");
        int i = 0;
        foreach (var c in canvases)
        {
            string info = $"[{i}] name={c.name} active={c.gameObject.activeInHierarchy} enabled={c.enabled} mode={c.renderMode}";
            if (c.renderMode == RenderMode.ScreenSpaceCamera)
                info += $" cam={(c.worldCamera ? c.worldCamera.name : "NULL")}";
            info += $" sortingOrder={c.sortingOrder} display={c.targetDisplay}";
            Debug.Log(info);
            i++;
        }
    }

    void Start()
    {
        Dump(); // imprime automáticamente al entrar en Play
    }
}
