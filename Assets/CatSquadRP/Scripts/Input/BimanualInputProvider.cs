using UnityEngine;

namespace CatSquad.RescateProfundo
{
    /// <summary>
    /// Fuente de entrada bimanual. El juego nunca lee teclado/webcam directamente:
    /// solo lee un BimanualFrame. Así la webcam (módulo de visión) y el teclado
    /// son intercambiables sin tocar las mecánicas.
    /// </summary>
    public abstract class BimanualInputProvider : MonoBehaviour
    {
        public abstract string DisplayName { get; }

        /// <summary>¿Tiene datos válidos en este momento?</summary>
        public abstract bool IsAvailable { get; }

        /// <summary>Llena el frame con la lectura actual.</summary>
        public abstract void Read(ref BimanualFrame frame, InputContext context, float dt);

        public virtual void BeginCalibration() { }
        public virtual void EndCalibration() { }
    }
}
