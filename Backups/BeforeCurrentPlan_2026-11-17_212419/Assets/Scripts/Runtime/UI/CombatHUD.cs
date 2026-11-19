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

        private void Start()
        {
            worldCamera = Camera.main;
            foreach (Combatant unit in simulation.Units)
            {
                if (unit.Team != player.Character.Team)
                {
                    target = unit;
                    break;
                }
            }
            simulation.Resolver.HitResolved += OnHit;
        }

        private void OnHit(HitResult result) => lastDamage = result.damage;

        private void LateUpdate()
        {
            int segment = player.Character.Combo.CurrentIndex + 1;
            string action = segment > 0 ? "普攻第 " + segment + " 段" : "待机";
            actionText.text = $"左键普攻 · WASD 移动 · Shift 跑步 · C 属性 · Esc 暂停\n{action}    最近命中 {lastDamage:0.#}";
            bool targetVisible = target != null && target.isActiveAndEnabled;
            targetText.gameObject.SetActive(targetVisible);
            if (!targetVisible)
                return;

            Vector3 screen = worldCamera.WorldToScreenPoint(target.transform.position + Vector3.up * 2.3f);
            targetText.gameObject.SetActive(screen.z > 0f);
            targetText.rectTransform.position = screen;
            targetText.text = $"{target.DisplayName}\n{target.Health.Current:0} / {target.Health.Max:0}" + (target.Health.IsDead ? "\n已击破" : "");
        }

        private void OnDestroy()
        {
            if (simulation != null && simulation.Resolver != null)
                simulation.Resolver.HitResolved -= OnHit;
        }
    }
}
