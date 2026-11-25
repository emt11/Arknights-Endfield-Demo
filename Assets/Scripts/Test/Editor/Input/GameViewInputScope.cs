using System;
using UnityEngine.InputSystem;

namespace Endfield.Input.Tests
{
    internal sealed class GameViewInputScope : IDisposable
    {
        private readonly InputSettings settings = InputSystem.settings;
        private readonly InputSettings.EditorInputBehaviorInPlayMode editorBehavior;
        private readonly InputSettings.BackgroundBehavior backgroundBehavior;

        public GameViewInputScope()
        {
            // 后台运行 Test Runner 时也要把虚拟键鼠事件送到 Game 视图
            editorBehavior = settings.editorInputBehaviorInPlayMode;
            backgroundBehavior = settings.backgroundBehavior;
            settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        }

        public void Dispose()
        {
            settings.editorInputBehaviorInPlayMode = editorBehavior;
            settings.backgroundBehavior = backgroundBehavior;
        }
    }
}
