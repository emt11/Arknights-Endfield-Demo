using System.IO;
using System.Linq;
using Endfield.AnimationSystem;
using Endfield.Character;
using Endfield.Combat;
using UnityEditor;
using UnityEngine;

namespace Endfield.EditorCombat
{
    public static class CombatDemoSetup
    {
        public static void ConfigureCombo()
        {
            Directory.CreateDirectory("Assets/ScriptObjects/Combat/Actions");
            Directory.CreateDirectory("Assets/ScriptObjects/Animations");
            var definitions = new ActionDefinition[3];
            float[] hitStarts = { .6f, 24f / 60f, .95f };
            float[] hitEnds = { .89f, 31f / 60f, 1.3f };
            for (int index = 0; index < definitions.Length; index++)
            {
                int number = index + 1;
                string path = "Assets/ScriptObjects/Combat/Actions/GreatSwordAttack" + number + ".asset";
                ActionDefinition definition = AssetDatabase.LoadAssetAtPath<ActionDefinition>(path);
                if (definition != null && definition.animation != null)
                {
                    definitions[index] = definition;
                    continue;
                }

                string animationPath = "Assets/ScriptObjects/Animations/GreatSwordAttack" + number + "Animation.asset";
                var animation = AssetDatabase.LoadAssetAtPath<AnimationDefinition>(animationPath);
                if (animation == null)
                {
                    animation = ScriptableObject.CreateInstance<AnimationDefinition>();
                    animation.clip = AssetDatabase.LoadAllAssetsAtPath("Assets/Game/Animations/Characters/Clips/GreatSword/Normal/GS_Attack" + number + ".fbx")
                        .OfType<AnimationClip>().First(clip => !clip.name.StartsWith("__preview__"));
                    animation.pointEvents = new[] { new ComboAdvancePoint { time = animation.Duration * .5f } };
                    animation.windowEvents = new TimelineWindowEvent[]
                    {
                        new HitWindow
                        {
                            start = hitStarts[index],
                            end = hitEnds[index],
                            damageMultiplier = index == 2 ? 1.8f : .8f + index * .2f,
                            hitboxIds = new[] { 0 }
                        },
                        new CancelWindow
                        {
                            start = animation.Duration * .6f,
                            end = animation.Duration,
                            categories = ActionCategory.Attack | ActionCategory.Move
                        }
                    };
                    AssetDatabase.CreateAsset(animation, animationPath);
                }
                if (definition == null)
                {
                    definition = ScriptableObject.CreateInstance<ActionDefinition>();
                    definition.animation = animation;
                    definition.rootMotion = AssetDatabase.LoadAssetAtPath<RootMotionDefinition>("Assets/ScriptObjects/Combat/RootMotion/GreatSwordAttack" + number + "Root.asset");
                    AssetDatabase.CreateAsset(definition, path);
                }
                else
                {
                    definition.animation = animation;
                    EditorUtility.SetDirty(definition);
                    AssetDatabase.SaveAssetIfDirty(definition);
                }
                definitions[index] = definition;
            }

            CharacterDefinition character = AssetDatabase.LoadAssetAtPath<CharacterDefinition>("Assets/ScriptObjects/Characters/PlayerCharacterAsset.asset");
            var serialized = new SerializedObject(character);
            SerializedProperty array = serialized.FindProperty("basicAttacks");
            if (array.arraySize == 0)
                array.arraySize = definitions.Length;
            for (int index = 0; index < Mathf.Min(array.arraySize, definitions.Length); index++)
            {
                SerializedProperty slot = array.GetArrayElementAtIndex(index);
                if (slot.objectReferenceValue == null)
                    slot.objectReferenceValue = definitions[index];
            }

            if (serialized.ApplyModifiedPropertiesWithoutUndo())
                AssetDatabase.SaveAssetIfDirty(character);
        }
    }
}
