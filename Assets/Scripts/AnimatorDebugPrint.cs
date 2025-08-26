using UnityEngine;

public class AnimatorDebugPrint : MonoBehaviour
{
    public Animator anim;
    public string[] probeStates = { "Creep|Idle1_Action", "Creep|Walk2_Action", "Creep|Punch_Action" };

    void Awake() { if (!anim) anim = GetComponent<Animator>(); }

    void Start()
    {
        var rc = anim ? anim.runtimeAnimatorController : null;
        Debug.Log($"[AnimDbg] GO={name} Controller={(rc ? rc.name : "<null>")}  Avatar={(anim && anim.avatar ? anim.avatar.name : "<null>")} valid={(anim && anim.avatar ? anim.avatar.isValid : false)}");

        var clips = rc ? rc.animationClips : null;
        Debug.Log($"[AnimDbg] ClipCount={(clips == null ? 0 : clips.Length)}");
        if (clips != null) foreach (var c in clips)
                Debug.Log($"[AnimDbg] Clip: {c.name}  legacy={c.legacy}");

        foreach (var st in probeStates)
            Debug.Log($"[AnimDbg] HasState '{st}' = {anim.HasState(0, Animator.StringToHash(st))}");
    }
}
