using System;
using System.Collections.Generic;
using Endfield.Actors;
using Endfield.Stats;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Endfield.UI
{
    public sealed class CharacterStatsPanel : MonoBehaviour
    {
        [Serializable]
        private struct SummaryStat
        {
            public StatId id;
            public TMP_Text value;
            public GameObject mainAttributeHighlight;
        }

        [SerializeField] private CharacterStats[] characters;
        [SerializeField] private GameObject panel;
        [SerializeField] private Button openButton;
        [SerializeField] private Button closeButton;
        [SerializeField] private Button switchButton;
        [SerializeField] private InputActionReference toggleAction;
        [SerializeField] private TMP_Text characterName;
        [SerializeField] private RectTransform content;
        [SerializeField] private RectTransform groupTemplate;
        [SerializeField] private TMP_Text rowTemplate;
        [SerializeField] private SummaryStat[] summaryStats = Array.Empty<SummaryStat>();

        private readonly Dictionary<StatId, (TMP_Text label, bool showName)> rows = new();
        private int selected;
        private IDisposable subscription;

        private void Awake()
        {
            if (characters == null || characters.Length == 0 || Array.Exists(characters, item => !item))
                throw new InvalidOperationException("角色属性页需要已配置的角色");

            foreach (var stat in summaryStats)
                rows.Add(stat.id, (stat.value, false));

            foreach (var group in StatCatalog.Groups)
            {
                RectTransform section = null;
                foreach (var definition in StatCatalog.GetByGroup(group))
                {
                    if (rows.ContainsKey(definition.Id)) continue;
                    if (!section)
                    {
                        section = Instantiate(groupTemplate, content);
                        section.name = group.ToString();
                        section.GetComponentInChildren<TMP_Text>(true).text = StatCatalog.GetGroupName(group);
                        section.gameObject.SetActive(true);
                    }
                    var row = Instantiate(rowTemplate, section);
                    row.name = definition.Id.ToString();
                    row.gameObject.SetActive(true);
                    rows.Add(definition.Id, (row, true));
                }
            }

            openButton.gameObject.SetActive(!panel.activeSelf);
        }

        public void Open()
        {
            panel.SetActive(true);
            openButton.gameObject.SetActive(false);
            BindCharacter();
        }

        public void Close()
        {
            subscription?.Dispose();
            subscription = null;
            panel.SetActive(false);
            openButton.gameObject.SetActive(true);
        }

        private void NextCharacter()
        {
            selected = (selected + 1) % characters.Length;
            BindCharacter();
        }

        private void BindCharacter()
        {
            subscription?.Dispose();
            var character = characters[selected];
            characterName.text = character.GetComponent<Combatant>().DisplayName;
            var stats = character.Stats;
            foreach (var item in summaryStats)
                if (item.mainAttributeHighlight) item.mainAttributeHighlight.SetActive(item.id == stats.MainAttribute);
            foreach (var id in rows.Keys)
                SetValue(id, stats.GetValue(id));
            subscription = stats.Subscribe(RefreshValues);
        }

        private void RefreshValues(IReadOnlyList<StatChange> changes)
        {
            foreach (var change in changes)
                SetValue(change.StatId, change.NewValue);
        }

        private void SetValue(StatId id, double value)
        {
            var definition = StatCatalog.Get(id);
            string number = definition.Unit == StatUnit.Ratio ? value.ToString("0.##%") : value.ToString("0.##");
            var row = rows[id];
            row.label.text = row.showName ? $"{definition.DisplayName}<pos=64%>{number}" : number;
        }

        private void OnEnable()
        {
            openButton.onClick.AddListener(Open);
            closeButton.onClick.AddListener(Close);
            if (characters.Length > 1) switchButton.onClick.AddListener(NextCharacter);

            if (panel.activeSelf) BindCharacter();
            if (toggleAction)
            {
                toggleAction.action.performed += TogglePanel;
                toggleAction.action.Enable();
            }
        }

        private void OnDisable()
        {
            openButton.onClick.RemoveListener(Open);
            closeButton.onClick.RemoveListener(Close);
            if (characters.Length > 1) switchButton.onClick.RemoveListener(NextCharacter);

            if (toggleAction)
            {
                toggleAction.action.performed -= TogglePanel;
                toggleAction.action.Disable();
            }
            subscription?.Dispose();
            subscription = null;
        }

        private void TogglePanel(InputAction.CallbackContext context)
        {
            if (panel.activeSelf) Close();
            else Open();
        }
    }
}
