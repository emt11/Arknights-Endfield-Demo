using System;
using Endfield.Resources;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Endfield.UI
{
    public sealed class HealthHud : MonoBehaviour
    {
        [SerializeField] private CharacterResources character;
        [SerializeField] private TMP_Text hpText;
        [SerializeField] private Image fill;
        private IDisposable subscription;

        public void Bind(CharacterResources target)
        {
            character = target ? target : throw new ArgumentNullException(nameof(target));
            if (isActiveAndEnabled) SubscribeToCharacter();
        }

        private void OnEnable()
        {
            if (!character || !hpText || !fill)
                throw new InvalidOperationException($"{name} 缺少血条目标、文本或填充图像");
            SubscribeToCharacter();
        }

        private void SubscribeToCharacter()
        {
            subscription?.Dispose();
            IReadOnlyCharacterResources resources = character.Container;
            Display(resources.Snapshot);
            subscription = resources.Subscribe(OnResourcesChanged);
        }

        private void OnResourcesChanged(ResourceSnapshot previous, ResourceSnapshot current)
        {
            if (previous.CurrentHP != current.CurrentHP || previous.MaxHP != current.MaxHP)
                Display(current);
        }

        private void Display(ResourceSnapshot snapshot)
        {
            hpText.text = $"{snapshot.CurrentHP:0.##} / {snapshot.MaxHP:0.##}";
            fill.fillAmount = snapshot.MaxHP > 0 ? (float)(snapshot.CurrentHP / snapshot.MaxHP) : 0;
        }

        private void OnDisable()
        {
            subscription?.Dispose();
            subscription = null;
        }
    }
}
