using System.Collections.Generic;
using UnityEngine;

namespace Core.Config
{
    [CreateAssetMenu(menuName = "Game/Config/Configs Container", fileName = "GameConfigsContainer")]
    public class GameConfigsContainer : ScriptableObject
    {
        [Tooltip("Список всех конфигов, которые должны быть зарегистрированы в DI")]
        public List<ScriptableObject> configs = new();
    }
}