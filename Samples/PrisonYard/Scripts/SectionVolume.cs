using System;
using UnityEngine;

namespace Project.Samples.PrisonYard
{
    /// <summary>
    /// The six places the prison is divided into. The enum name is also the identifier the model
    /// uses as an action argument, so these must stay identifier-shaped.
    /// </summary>
    public enum PrisonSection
    {
        CellBlock = 0,
        Yard = 1,
        Cafeteria = 2,
        Workshop = 3,
        Infirmary = 4,
        ControlRoom = 5
    }

    /// <summary>Helpers for turning section names to and from the strings the model emits.</summary>
    public static class PrisonSections
    {
        /// <summary>Every section, in a fixed order so prompts and lists are stable between decisions.</summary>
        public static readonly PrisonSection[] All =
        {
            PrisonSection.CellBlock,
            PrisonSection.Yard,
            PrisonSection.Cafeteria,
            PrisonSection.Workshop,
            PrisonSection.Infirmary,
            PrisonSection.ControlRoom
        };

        /// <summary>Sections prisoners are allowed into at all. The control room is staff only.</summary>
        public static readonly PrisonSection[] PrisonerAccessible =
        {
            PrisonSection.CellBlock,
            PrisonSection.Yard,
            PrisonSection.Cafeteria,
            PrisonSection.Workshop,
            PrisonSection.Infirmary
        };

        /// <summary>Parses a section name the model emitted. Case-insensitive, false when unknown.</summary>
        public static bool TryParse(string id, out PrisonSection section)
        {
            return Enum.TryParse(id, true, out section) && Array.IndexOf(All, section) >= 0;
        }

        /// <summary>The name the model uses for this section.</summary>
        public static string Id(this PrisonSection section)
        {
            return section.ToString();
        }
    }

    /// <summary>
    /// The rectangle of ground one section occupies. The status board asks these which section a
    /// person is standing in, so occupancy is a plain geometric question with no physics and no
    /// triggers, which also makes it testable without a scene.
    /// </summary>
    [AddComponentMenu("PrisonYard/Section Volume")]
    public class SectionVolume : MonoBehaviour
    {
        [Tooltip("Which of the prison's six sections this rectangle covers. One volume per section.")]
        public PrisonSection section = PrisonSection.Yard;

        [Tooltip("Width and depth of the section in metres, centred on this object. Height is " +
                 "ignored: everyone is on the ground.")]
        public Vector2 size = new Vector2(20f, 14f);

        /// <summary>True when a world position is inside this section's footprint.</summary>
        public bool Contains(Vector3 worldPosition)
        {
            Vector3 c = transform.position;
            return Mathf.Abs(worldPosition.x - c.x) <= size.x * 0.5f
                && Mathf.Abs(worldPosition.z - c.z) <= size.y * 0.5f;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.3f, 0.7f, 1f, 0.35f);
            Gizmos.DrawWireCube(transform.position, new Vector3(size.x, 0.2f, size.y));
        }
    }
}
