using UnityEngine;
using UnityEngine.EventSystems;

// Full-screen invisible drag catcher behind the joystick/buttons: touch-drag anywhere
// on it accumulates a screen-space delta, consumed once per frame by ThirdPersonCamera,
// the same way PUBG Mobile's look area works (finger down anywhere, drag to turn).
public class TouchLookArea : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    private Vector2 _lastPosition;
    private Vector2 _accumulatedDelta;
    private bool _dragging;

    public Vector2 ConsumeDelta()
    {
        Vector2 delta = _accumulatedDelta;
        _accumulatedDelta = Vector2.zero;
        return delta;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        _dragging = true;
        _lastPosition = eventData.position;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!_dragging) return;

        _accumulatedDelta += eventData.position - _lastPosition;
        _lastPosition = eventData.position;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        _dragging = false;
    }
}
