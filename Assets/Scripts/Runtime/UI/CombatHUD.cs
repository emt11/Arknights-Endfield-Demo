using Endfield.Character;
using Endfield.Combat;
using Endfield.Controller;
using TMPro;
using UnityEngine;

namespace Endfield.UI
{
    [DefaultExecutionOrder(100)]
    public sealed class CombatHUD : MonoBehaviour
    {
        [SerializeField] private CombatSimulation simulation;
        [SerializeField] private PlayerController player;
        [SerializeField] private TMP_Text actionText;
        [SerializeField] private TMP_Text targetText;
        private Camera worldCamera;
        private Combatant target;
        private float lastDamage;
        private CharacterHealth targetHealth;

        private void Start()
        {
            worldCamera = Camera.main;
            foreach (Combatant unit in simulation.Units)
            {
                if (unit.Team != player.Character.Team)
                {
                    target = unit;
                    targetHealth = unit.Health;
                    targetHealth.Changed += OnHealthChanged;
                    RefreshTarget();
                    break;
                }
            }
            simulation.Resolver.HitResolved += OnHit;
            player.Character.Actions.ResultPublished += OnAction;
            RefreshAction();
        }

        private void OnHit(HitResult result)
        {
            lastDamage = result.damage;
            RefreshAction();
        }

        private void OnAction(ActionResult result) => RefreshAction();

        private void OnHealthChanged(HealthChange change) => RefreshTarget();

        private void RefreshTarget()
        {
            if (target != null)
                targetText.text = $"{target.DisplayName}\n{targetHealth.Current:0} / {targetHealth.Max:0}" + (targetHealth.IsDead ? "\n已击破" : "");
        }

        private void RefreshAction()
        {
            int segment = player.Character.Combo.CurrentIndex + 1;
            string action = segment > 0 ? "普攻第 " + segment + " 段" : "待机";
            actionText.text = $"左键普攻 · WASD 移动 · Shift 跑步 · C 属性 · Esc 暂停\n{action}    最近命中 {lastDamage:0.#}";
        }

        private void LateUpdate()
        {
            bool visible = target != null && target.isActiveAndEnabled;
            if (visible)
            {
                Vector3 screen = worldCamera.WorldToScreenPoint(target.transform.position + Vector3.up * 2.3f);
                visible = screen.z > 0f;
                if (visible)
                    targetText.rectTransform.position = screen;
            }
            if (targetText.gameObject.activeSelf != visible)
                targetText.gameObject.SetActive(visible);
        }

        private void OnDestroy()
        {
            if (targetHealth != null)
                targetHealth.Changed -= OnHealthChanged;
            if (player != null && player.Character != null && player.Character.Actions != null)
                player.Character.Actions.ResultPublished -= OnAction;
            if (simulation != null && simulation.Resolver != null)
                simulation.Resolver.HitResolved -= OnHit;
        }
    }
}
