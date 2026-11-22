using Endfield.Stats;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Endfield.Demo
{
    public sealed class WoodenSwordPreview : MonoBehaviour
    {
        [SerializeField] private CharacterStats character;
        [SerializeField] private Button button;
        [SerializeField] private TMP_Text label;
        private static readonly SourceID WoodenSword = new("Equipment", 17);
        private bool equipped;

        private void Awake()
        {
            button.onClick.AddListener(Toggle);
            UpdateLabel();
        }

        private void Toggle()
        {
            var result = equipped
                ? character.Stats.RemoveSource(WoodenSword)
                : character.Stats.ReplaceSource(WoodenSword, new StatModifier(StatId.STR, ModifierOperation.Flat, 3),
                    new StatModifier(StatId.WeaponATK, ModifierOperation.Flat, 3));
            if (!result.Success)
            {
                Debug.LogError(result.Error, this);
                return;
            }

            equipped = !equipped;
            UpdateLabel();
        }

        private void UpdateLabel()
        {
            label.text = character.DisplayName + (equipped ? " · 取下木剑" : " · 装备木剑");
        }
    }
}
