using UnityEngine;
using UnityEngine.EventSystems;

public class ButtonHoverEffect : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public Animator streakAnimator;
    public CanvasGroup streakGroup;
    public AudioSource hoverAudio;

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (streakAnimator != null)
            streakAnimator.SetBool("IsHovered", true);

        if (streakGroup != null)
            streakGroup.alpha = 1;

        if (hoverAudio != null)
            hoverAudio.Play();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (streakAnimator != null)
            streakAnimator.SetBool("IsHovered", false);

        if (streakGroup != null)
            streakGroup.alpha = 0;
    }


}
