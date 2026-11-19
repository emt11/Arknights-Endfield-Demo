using System.IO;
using System.Linq;
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
            Directory.CreateDirectory("Assets/Game/Combat/Actions");
            var definitions = new ActionDefinition[3];
            for (int index = 0; index < definitions.Length; index++)
            {
                int number = index + 1;
                string path = "Assets/Game/Combat/Actions/GreatSwordAttack" + number + ".asset";
                ActionDefinition definition = AssetDatabase.LoadAssetAtPath<ActionDefinition>(path);
                if (definition == null)
                {
                    definition = ScriptableObject.CreateInstance<ActionDefinition>();
                    AssetDatabase.CreateAsset(definition, path);
                }

                definition.Clip = AssetDatabase.LoadAllAssetsAtPath("Assets/Game/Animations/Characters/Clips/GreatSword/Normal/GS_Attack" + number + ".fbx")
                    .OfType<AnimationClip>().First(clip => !clip.name.StartsWith("__preview__"));
                definition.ClipEnd = definition.Clip.length;
                definition.Points = new[] { new ActionPoint { Time = definition.Duration * 0.5f, Kind = ActionPointKind.ComboAdvance } };
                definition.CancelWindows = new[] { new CancelWindow { Start = definition.Duration * 0.6f, End = definition.Duration, Categories = ActionCategory.Attack | ActionCategory.Move } };
                definition.HitWindows = new[] { new HitWindow { Start = 0.25f, End = 0.65f, Group = 0, DamageMultiplier = index == 2 ? 1.8f : 0.8f + index * 0.2f } };
                EditorUtility.SetDirty(definition);
                definitions[index] = definition;
            }

            CharacterDefinition character = AssetDatabase.LoadAssetAtPath<CharacterDefinition>("Assets/ScriptObjects/Characters/PlayerCharacterAsset.asset");
            var serialized = new SerializedObject(character);
            SerializedProperty array = serialized.FindProperty("basicAttacks");
            array.arraySize = definitions.Length;
            for (int index = 0; index < definitions.Length; index++)
                array.GetArrayElementAtIndex(index).objectReferenceValue = definitions[index];

            serialized.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
        }
    }
}
