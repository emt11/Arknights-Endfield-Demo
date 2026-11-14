using Endfield.Character;
using Endfield.Controller;
using UnityEngine;

namespace Endfield.UI
{
    public sealed class GameUIController : MonoBehaviour
    {
        [SerializeField] private Combatant target;
        [SerializeField] private GameInputManager inputManager;
        [SerializeField] private HealthBarUI healthHUD;
        [SerializeField] private CharacterStatsPanelUI statsPanel;

        private CharacterHealth boundHealth;
        private CharacterStats boundStats;
        private bool started;

        private void Awake() => statsPanel.SetVisible(false);

        private void OnEnable()
        {
            inputManager.CharacterPanelToggleRequested += TogglePanel;
            inputManager.CharacterPanelCloseRequested += ClosePanel;
            statsPanel.CloseButton.onClick.AddListener(ClosePanel);
            if (started)
                BindTarget();
        }

        private void Start()
        {
            started = true;
            BindTarget();
        }

        private void BindTarget()
        {
            UnbindTarget();
            boundHealth = target.Health;
            boundStats = target.Stats;
            boundHealth.Changed += OnHealthChanged;
            boundStats.Changed += OnStatsChanged;
            healthHUD.Refresh(boundHealth.Current, boundHealth.Max);
            if (statsPanel.IsOpen)
                RefreshPanel();
        }

        private void UnbindTarget()
        {
            if (boundHealth != null)
                boundHealth.Changed -= OnHealthChanged;

            if (boundStats != null)
                boundStats.Changed -= OnStatsChanged;

            boundHealth = null;
            boundStats = null;
        }

        private void OnHealthChanged(HealthChange change) => healthHUD.Refresh(change.Current, change.Max);

        private void OnStatsChanged(StatChange change)
        {
            if (statsPanel.IsOpen)
                statsPanel.RefreshStats(boundStats);
        }

        private void RefreshPanel()
        {
            statsPanel.SetCharacterName(target.DisplayName);
            statsPanel.RefreshStats(boundStats);
        }

        private void TogglePanel()
        {
            if (statsPanel.IsOpen)
            {
                ClosePanel();
                return;
            }

            RefreshPanel();
            statsPanel.SetVisible(true);
            inputManager.SetCharacterPanelOpen(true);
        }

        private void ClosePanel()
        {
            statsPanel.SetVisible(false);
            inputManager.SetCharacterPanelOpen(false);
        }

        private void OnDisable()
        {
            if (inputManager != null)
            {
                inputManager.CharacterPanelToggleRequested -= TogglePanel;
                inputManager.CharacterPanelCloseRequested -= ClosePanel;
                inputManager.SetCharacterPanelOpen(false);
            }

            if (statsPanel != null)
            {
                statsPanel.CloseButton.onClick.RemoveListener(ClosePanel);
                statsPanel.SetVisible(false);
            }

            UnbindTarget();
        }
    }
}
