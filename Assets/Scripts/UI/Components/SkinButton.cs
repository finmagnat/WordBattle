using Core.Config;
using UnityEngine;
using UnityEngine.UI;

namespace Core.UI.Components
{
    public class SkinButton : MonoBehaviour
    {
        public SkinType SkinType;
        
        public Button button;
        
        [SerializeField] private Image _imageCheck;
        
        public void SetActiveStatus(bool status)
        {
            _imageCheck.gameObject.SetActive(status);
        }
    }
}