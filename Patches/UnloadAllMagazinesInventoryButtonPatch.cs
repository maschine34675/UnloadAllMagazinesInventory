using EFT.Communications;
using EFT.InventoryLogic;
using EFT.UI;
using EFT.UI.DragAndDrop;
using HarmonyLib;
using SPT.Reflection.Patching;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UnloadAllMagazinesInventory.Patches
{
    internal class UnloadAllMagazinesInventoryButtonPatch : ModulePatch
    {
        private const string ButtonName = "UnloadAllMagazinesInventoryButton";

        private static readonly EquipmentSlot[] TargetSlots =
        {
            EquipmentSlot.TacticalVest,
            EquipmentSlot.Pockets,
            EquipmentSlot.Backpack,
            EquipmentSlot.SecuredContainer,
        };

        private static readonly FieldInfo _fieldDictionary =
            AccessTools.Field(typeof(ContainersPanel), "dictionary_0");

        private static readonly FieldInfo _fieldSearchableItemView =
            AccessTools.Field(typeof(SearchableSlotView), "_searchableItemView");

        private static readonly FieldInfo _fieldContainedGridsView =
            AccessTools.Field(typeof(SearchableItemView), "containedGridsView_0");

        private static readonly FieldInfo _fieldCompoundItem =
            AccessTools.Field(typeof(SearchableItemView), "compoundItem_0");

        private static readonly FieldInfo _fieldGridWindowTemplate =
            AccessTools.Field(typeof(ItemUiContext), "_gridWindowTemplate");

        private static readonly FieldInfo _fieldSortPanel =
            AccessTools.Field(typeof(GridWindow), "_sortPanel");

        private static readonly FieldInfo _fieldButton =
            AccessTools.Field(typeof(GridSortPanel), "_button");

        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(ContainersPanel), "Show");

        [PatchPostfix]
        static void Postfix(ContainersPanel __instance, InventoryController inventoryController)
        {
            var dict = _fieldDictionary.GetValue(__instance) as Dictionary<EquipmentSlot, SlotView>;
            if (dict == null)
            {
                Plugin.Log.LogWarning("[UnloadAllMagazinesInventory] dictionary_0 is null");
                return;
            }

            foreach (var slot in TargetSlots)
            {
                if (!dict.TryGetValue(slot, out var slotView)) continue;

                AttachButton(slotView, inventoryController);

                slotView.AddDisposable(
                    slotView.Slot.ReactiveContainedItem.Subscribe(_ =>
                        slotView.WaitForEndOfFrame(() => AttachButton(slotView, inventoryController))));
            }
        }

        static void AttachButton(SlotView slotView, InventoryController controller)
        {
            var searchableSlotView = slotView as SearchableSlotView;
            if (searchableSlotView == null)
            {
                Plugin.Log.LogWarning($"[UnloadAllMagazinesInventory] SlotView is no SearchableSlotView: {slotView?.GetType().Name}");
                return;
            }

            var searchableItemView = _fieldSearchableItemView.GetValue(searchableSlotView) as SearchableItemView;
            if (searchableItemView == null)
            {
                Plugin.Log.LogWarning("[UnloadAllMagazinesInventory] _searchableItemView is null");
                return;
            }

            var gridsView = _fieldContainedGridsView.GetValue(searchableItemView) as ContainedGridsView;
            if (gridsView == null) return;

            var container = _fieldCompoundItem.GetValue(searchableItemView) as CompoundItem;
            if (container == null) return;

            var existing = gridsView.transform.Find(ButtonName);
            if (existing != null) UnityEngine.Object.Destroy(existing.gameObject);

            var gridWindow = _fieldGridWindowTemplate.GetValue(ItemUiContext.Instance) as GridWindow;
            if (gridWindow == null) { Plugin.Log.LogWarning("[UnloadAllMagazinesInventory] _gridWindowTemplate null"); return; }

            var sortPanel = _fieldSortPanel.GetValue(gridWindow) as GridSortPanel;
            if (sortPanel == null) { Plugin.Log.LogWarning("[UnloadAllMagazinesInventory] _sortPanel null"); return; }

            var sortButton = _fieldButton.GetValue(sortPanel) as Button;
            if (sortButton == null) { Plugin.Log.LogWarning("[UnloadAllMagazinesInventory] _button null"); return; }

            var go = UnityEngine.Object.Instantiate(sortButton.gameObject, gridsView.transform, false);
            go.name = ButtonName;
            go.SetActive(true);

            foreach (var cg in go.GetComponentsInChildren<CanvasGroup>(true))
            {
                cg.alpha = 1f;
                cg.interactable = true;
                cg.blocksRaycasts = true;
            }

            var layout = go.GetComponent<LayoutElement>();
            if (layout != null) layout.ignoreLayout = true;

            var rect = go.GetComponent<RectTransform>();
            if (rect != null)
            {
                rect.anchorMin = Vector2.one;
                rect.anchorMax = Vector2.one;
                rect.pivot = Vector2.one;
                rect.anchoredPosition = Vector2.zero;
            }

            go.transform.SetAsLastSibling();

            var sprite = CacheResourcesPopAbstractClass.Pop<Sprite>("Characteristics/Icons/UnloadAmmo");
            if (sprite != null)
            {
                foreach (var img in go.GetComponentsInChildren<Image>())
                {
                    if (img.gameObject != go && img.sprite != null)
                    {
                        img.sprite = sprite;
                        img.preserveAspect = true;
                        break;
                    }
                }
            }

            var btn = go.AddComponent<UnloadButton>();
            btn.Setup(controller, container);

            var unityBtn = go.GetComponent<Button>();
            if (unityBtn != null)
            {
                unityBtn.onClick.RemoveAllListeners();
                unityBtn.onClick.AddListener(btn.OnClick);
            }
            else
            {
                Plugin.Log.LogWarning("[UnloadAllMagazinesInventory] No Button-Component on cloned object");
            }

            Plugin.Log.LogInfo($"[UnloadAllMagazinesInventory] Button added for {container.Template._name}");
        }


        internal static async Task UnloadAllMagazinesAsync(InventoryController controller, CompoundItem root)
        {
            var magazines = new List<MagazineItemClass>();
            CollectMagazines(root, magazines);

            int count = 0;
            foreach (var mag in magazines)
            {
                if (mag.Count <= 0) continue;
                var result = await controller.UnloadMagazine(mag, false);
                if (!result.Failed)
                    count++;
                else
                    Plugin.Log.LogWarning($"[UnloadAllMagazinesInventory] {mag.Template._name}: {result.Error}");
            }

            NotificationManagerClass.DisplayMessageNotification(
                $"{count} Magazine(s) unloaded.",
                ENotificationDurationType.Default);
        }

        static void CollectMagazines(CompoundItem item, List<MagazineItemClass> result)
        {
            foreach (var grid in item.Grids ?? new StashGridClass[0])
                foreach (var gridItem in grid.Items)
                {
                    if (gridItem is MagazineItemClass mag) result.Add(mag);
                    else if (gridItem is CompoundItem sub) CollectMagazines(sub, result);
                }

            foreach (var slot in item.Slots ?? new Slot[0])
            {
                var slotItem = slot.ContainedItem;
                if (slotItem == null) continue;
                if (slotItem is MagazineItemClass mag) result.Add(mag);
                else if (slotItem is CompoundItem sub) CollectMagazines(sub, result);
            }
        }


        private class UnloadButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IEventSystemHandler
        {
            private InventoryController _controller;
            private CompoundItem _container;

            public void Setup(InventoryController controller, CompoundItem container)
            {
                _controller = controller;
                _container = container;
            }

            public void OnClick()
            {
                var button = GetComponent<Button>();
                if (button != null) button.interactable = false;

                UnloadAllMagazinesAsync(_controller, _container)
                    .ContinueWith(t =>
                    {
                        if (button != null) button.interactable = true;
                        if (t.IsFaulted)
                            Plugin.Log.LogError($"[UnloadAllMagazinesInventory] Error: {t.Exception}");
                    });
            }

            public void OnPointerEnter(PointerEventData _)
                => ItemUiContext.Instance.Tooltip.Show("Unload all magazines", null, 0f, null);

            public void OnPointerExit(PointerEventData _)
                => ItemUiContext.Instance.Tooltip.Close();
        }
    }
}
