using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace SaintsGraph.Editor
{
    /// <summary>
    /// Opt-in click logger for investigating input problems inside node bodies, where three
    /// event systems meet (GraphView manipulators, UIToolkit controls, IMGUI fallbacks) and
    /// symptoms like "this checkbox cannot be clicked" are otherwise unobservable. While
    /// enabled, every press/release in a graph window logs what the pointer actually hit,
    /// who holds the real (leaf) keyboard focus and who captured the pointer.
    /// Toggle via Tools &gt; SaintsGraph &gt; Interaction Diagnostics; off by default and
    /// per-session, so it never ships noise.
    /// </summary>
    internal static class InteractionDiagnostics
    {
        private const string MenuPath = "Tools/SaintsGraph/Interaction Diagnostics";
        private const string StateKey = "SaintsGraph.InteractionDiagnostics";

        private static MethodInfo leafFocusMethod;

        internal static bool Enabled => SessionState.GetBool(StateKey, false);

        [MenuItem(MenuPath)]
        private static void Toggle()
        {
            SessionState.SetBool(StateKey, !Enabled);
            Debug.Log($"[SaintsGraph] Interaction diagnostics {(Enabled ? "enabled - click the misbehaving control, then read the log" : "disabled")}.");
        }

        [MenuItem(MenuPath, true)]
        private static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, Enabled);
            return true;
        }

        /// <summary>Registered unconditionally by the graph window; a disabled logger costs one bool check per press.</summary>
        internal static void Install(VisualElement root)
        {
            root.RegisterCallback<PointerDownEvent>(evt => Log("press", root, evt.position), TrickleDown.TrickleDown);
            root.RegisterCallback<PointerUpEvent>(evt => Log("release", root, evt.position), TrickleDown.TrickleDown);
        }

        /// <summary>
        /// IMGUI fallbacks (custom OnGUI-only PropertyDrawers) are opaque to element-level
        /// logging: a dead checkbox inside one can mean the mouse events never arrived, the
        /// hot control never armed, or the drawer threw mid-pass. While diagnostics are on,
        /// the container under a press gets its OnGUI wrapped to log every non-layout pass:
        /// event type, mouse position, hotControl before/after and exceptions. A healthy
        /// click reads "mouseDown hot 0-&gt;N" then "mouseUp hot N-&gt;0".
        /// </summary>
        private static readonly ConditionalWeakTable<IMGUIContainer, object> WrappedContainers = new();

        private static void WrapImgui(IMGUIContainer container)
        {
            if (WrappedContainers.TryGetValue(container, out _))
            {
                return;
            }

            WrappedContainers.Add(container, null);
            Action original = container.onGUIHandler;
            container.onGUIHandler = () =>
            {
                EventType type = Event.current?.type ?? EventType.Ignore;
                Vector2 mouse = Event.current?.mousePosition ?? Vector2.zero;
                int hotBefore = GUIUtility.hotControl;
                try
                {
                    original?.Invoke();
                }
                catch (Exception exception)
                {
                    if (Enabled)
                    {
                        Debug.LogError($"[SaintsGraph diag] imgui OnGUI threw on {type}: {exception.GetType().Name}: {exception.Message}");
                    }

                    throw;
                }

                if (Enabled && type != EventType.Layout && type != EventType.Repaint)
                {
                    Debug.Log($"[SaintsGraph diag] imgui pass {type} mouse={mouse:F0} hot {hotBefore}->{GUIUtility.hotControl} kb={GUIUtility.keyboardControl}");
                }
            };
        }

        private static void Log(string phase, VisualElement root, Vector2 position)
        {
            if (!Enabled || root.panel == null)
            {
                return;
            }

            IPanel panel = root.panel;
            StringBuilder line = new StringBuilder("[SaintsGraph diag] ").Append(phase)
                .Append(" at ").Append(position.ToString("F0")).Append('\n');

            line.Append("  hit: ");
            VisualElement picked = panel.Pick(position);
            for (VisualElement current = picked; current != null; current = current.parent)
            {
                if (current is IMGUIContainer imguiContainer)
                {
                    WrapImgui(imguiContainer);
                    break;
                }
            }
            int depth = 0;
            for (VisualElement current = picked; current != null && depth < 7; current = current.parent, depth++)
            {
                if (depth > 0)
                {
                    line.Append(" < ");
                }

                Append(line, current);
            }

            line.Append('\n');
            line.Append("  leaf focus: ");
            Append(line, LeafFocused(panel));
            line.Append('\n');
            line.Append("  pointer capture: ");
            Append(line, panel.GetCapturingElement(PointerId.mousePointerId) as VisualElement);
            Debug.Log(line.ToString());
        }

        /// <summary>
        /// focusController.focusedElement retargets its answer to the outermost composite root
        /// (a ListView reports itself while its inner text input really holds focus), so the
        /// truthful leaf has to be read via the internal API.
        /// </summary>
        private static VisualElement LeafFocused(IPanel panel)
        {
            FocusController controller = panel.focusController;
            if (controller == null)
            {
                return null;
            }

            leafFocusMethod ??= controller.GetType().GetMethod(
                "GetLeafFocusedElement", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return leafFocusMethod?.Invoke(controller, null) as VisualElement
                   ?? controller.focusedElement as VisualElement;
        }

        private static void Append(StringBuilder into, VisualElement element)
        {
            if (element == null)
            {
                into.Append("(none)");
                return;
            }

            into.Append(element.GetType().Name);
            if (!string.IsNullOrEmpty(element.name))
            {
                into.Append('#').Append(element.name);
            }

            foreach (string className in element.GetClasses())
            {
                into.Append('.').Append(className);
            }
        }
    }
}
