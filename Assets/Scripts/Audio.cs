using UnityEngine;

public class AudioHub : MonoBehaviour
{
    public static AudioHub I; // acceso rápido (instancia por escena)

    [Header("Sources (2D)")]
    public AudioSource uiSource;     
    public AudioSource sfxSource;     
    public AudioSource ambientSource; 

    [Header("Ambiente")]
    public bool useMenuAmbient = false; 
    public AudioClip ambientMenu;
    public AudioClip ambientGame;

    [Header("UI")]
    public AudioClip uiClick;
    public AudioClip uiHover;

    [Header("Jugador / Juego")]
    public AudioClip flashlightOn;
    public AudioClip flashlightOff;
    public AudioClip[] footstepClips;

    [Header("Pickups (opcional)")]
    public AudioClip pickupSoul;    
    public AudioClip pickupBattery; 

    void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;
    }

    void Start()
    {
        if (!ambientSource) return;
        var clip = useMenuAmbient ? ambientMenu : ambientGame;
        if (clip)
        {
            ambientSource.clip = clip;
            ambientSource.loop = true;
            ambientSource.Play();
        }
    }

    // -------- UI --------
    public void PlayUIClick() { if (uiSource && uiClick) uiSource.PlayOneShot(uiClick); }
    public void PlayUIHover() { if (uiSource && uiHover) uiSource.PlayOneShot(uiHover); }

    // -------- SFX --------
    public void PlaySFX(AudioClip clip, float vol = 1f)
    { if (sfxSource && clip) sfxSource.PlayOneShot(clip, vol); }

    public void PlayFlashlightOn() { PlaySFX(flashlightOn); }
    public void PlayFlashlightOff() { PlaySFX(flashlightOff); }

    public void PlayFootstep()
    {
        if (!sfxSource || footstepClips == null || footstepClips.Length == 0) return;
        var c = footstepClips[Random.Range(0, footstepClips.Length)];
        if (c) sfxSource.PlayOneShot(c, 1f);
    }

    public void PlayPickupSoul() { PlaySFX(pickupSoul); }
    public void PlayPickupBattery() { PlaySFX(pickupBattery); }

    // 3D en el mundo (monstruos, etc.)
    public static void PlayAt(AudioClip clip, Vector3 pos, float vol = 1f)
    {
        if (clip != null) AudioSource.PlayClipAtPoint(clip, pos, vol);
    }
}