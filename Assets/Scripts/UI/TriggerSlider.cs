using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Slider yang ramah gamepad. Saat memakai gamepad, kiri/kanan (D-pad atau stik) memindah pilihan seperti tombol biasa,
// dan nilainya diubah dengan LT (turun) / RT (naik). Tahan trigger untuk mengubah terus.
// Saat memakai keyboard, panah kiri/kanan tetap mengubah nilai seperti slider bawaan.
public class TriggerSlider : Slider
{
    private const float Step = 0.1f;
    private const float RepeatDelay = 0.4f;
    private const float RepeatInterval = 0.08f;
    private const float PressThreshold = 0.5f;

    private int heldDirection;
    private float nextRepeat;

    public override void OnMove(AxisEventData eventData)
    {
        if (!IsActive() || !IsInteractable())
        {
            base.OnMove(eventData);
            return;
        }

        // Keyboard: panah kiri/kanan selalu mengubah nilai, walau slider punya tetangga kiri/kanan.
        if (!ControlScheme.IsGamepad)
        {
            if (eventData.moveDir == MoveDirection.Left)
                Nudge(-1);
            else if (eventData.moveDir == MoveDirection.Right)
                Nudge(1);
            else
                base.OnMove(eventData);

            return;
        }

        Selectable target = null;

        switch (eventData.moveDir)
        {
            case MoveDirection.Left:
                target = navigation.mode == Navigation.Mode.Explicit ? navigation.selectOnLeft : FindSelectable(transform.rotation * Vector3.left);
                break;
            case MoveDirection.Right:
                target = navigation.mode == Navigation.Mode.Explicit ? navigation.selectOnRight : FindSelectable(transform.rotation * Vector3.right);
                break;
            default:
                base.OnMove(eventData);
                return;
        }

        if (target != null && target.IsActive() && target.IsInteractable())
            eventData.selectedObject = target.gameObject;
    }

    protected override void Update()
    {
        base.Update();

        if (!Application.isPlaying)
            return;

        bool selected = EventSystem.current != null && EventSystem.current.currentSelectedGameObject == gameObject;
        int direction = selected && IsInteractable() && EventSystem.current.sendNavigationEvents ? TriggerDirection() : 0;

        if (direction == 0)
        {
            heldDirection = 0;
            return;
        }

        float now = Time.unscaledTime;

        if (direction != heldDirection)
        {
            heldDirection = direction;
            nextRepeat = now + RepeatDelay;
            Nudge(direction);
        }
        else if (now >= nextRepeat)
        {
            nextRepeat = now + RepeatInterval;
            Nudge(direction);
        }
    }

    private void Nudge(int direction)
    {
        float range = maxValue - minValue;
        value = Mathf.Clamp(value + direction * Step * range, minValue, maxValue);
    }

    private static int TriggerDirection()
    {
        bool left = false;
        bool right = false;

        foreach (Gamepad pad in Gamepad.all)
        {
            left |= pad.leftTrigger.ReadValue() > PressThreshold;
            right |= pad.rightTrigger.ReadValue() > PressThreshold;
        }

        if (left == right)
            return 0;

        return right ? 1 : -1;
    }
}
