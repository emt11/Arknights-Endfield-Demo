using Endfield.Character;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Endfield.UI
{
    public sealed class CharacterStatsPanelUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text characterNameText;
        [SerializeField] private TMP_Text maxHPText;
        [SerializeField] private TMP_Text attackText;
        [SerializeField] private TMP_Text defenseText;
        [SerializeField] private Button closeButton;

        public bool IsOpen => gameObject.activeSelf;
        public Button CloseButton => closeButton;

        public void SetVisible(bool visible) => gameObject.SetActive(visible);

        public void SetCharacterName(string characterName)
        {
            characterNameText.text = characterName;
        }

        public void RefreshStats(CharacterStats stats)
        {
            maxHPText.text = FormatStat(stats, StatId.MaxHP);
            attackText.text = FormatStat(stats, StatId.Attack);
            defenseText.text = FormatStat(stats, StatId.Defense);
        }

        private static string FormatStat(CharacterStats stats, StatId id)
        {
            float value = stats.GetValue(id);
            float bonus = Mathf.Round((value - stats.GetBaseValue(id)) * 10f) / 10f;
            string text = value.ToString("0.#");
            if (bonus == 0f) return text;

            string color = bonus > 0f ? "#208A55" : "#C44343";
            string change = bonus.ToString("+0.#;-0.#;0");
            return $"{text}  <size=75%><color={color}>{change}</color></size>";
        }
    }
}