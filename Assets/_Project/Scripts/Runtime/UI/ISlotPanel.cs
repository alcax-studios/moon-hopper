using UnityEngine;
using UnityEngine.EventSystems;

namespace Alcax.UI
{
    /// <summary>What an ItemSlotView reports to: the inventory tab, the loot window, the trade window.</summary>
    public interface ISlotPanel
    {
        void Hover(ItemSlotView v);
        void Unhover(ItemSlotView v);
        void Clicked(ItemSlotView v, PointerEventData.InputButton button, Vector2 screen);
        bool BeginDrag(ItemSlotView v, Vector2 screen);
        void DragTo(Vector2 screen);
        void EndDrag(ItemSlotView from, ItemSlotView to, Vector2 screen);
    }
}
