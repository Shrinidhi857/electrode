#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace SmartLines.Editor
{
    public static class SmartLineMenu
    {
        [MenuItem("GameObject/2D Object/Smart Line (Lucidchart Style)", false, 10)]
        public static void CreateSmartLineDemo(MenuCommand menuCommand)
        {
            // 1. Root object
            GameObject root = new GameObject("SmartLine_System");
            Undo.RegisterCreatedObjectUndo(root, "Create Smart Line");

            // 2. Start Anchor
            GameObject startObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            startObj.name = "StartAnchor";
            startObj.transform.SetParent(root.transform, false);
            startObj.transform.position = new Vector3(-3f, 0f, 0f);
            startObj.transform.localScale = Vector3.one * 0.25f;
            // Replace 3D collider with 2D CircleCollider
            Object.DestroyImmediate(startObj.GetComponent<Collider>());
            startObj.AddComponent<CircleCollider2D>().radius = 0.5f;
            SmartLineAnchor startAnchor = startObj.AddComponent<SmartLineAnchor>();

            // 3. End Anchor
            GameObject endObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            endObj.name = "EndAnchor";
            endObj.transform.SetParent(root.transform, false);
            endObj.transform.position = new Vector3(3f, 0f, 0f);
            endObj.transform.localScale = Vector3.one * 0.25f;
            Object.DestroyImmediate(endObj.GetComponent<Collider>());
            endObj.AddComponent<CircleCollider2D>().radius = 0.5f;
            SmartLineAnchor endAnchor = endObj.AddComponent<SmartLineAnchor>();

            // 4. Line Renderer object
            GameObject lineObj = new GameObject("SmartLine");
            lineObj.transform.SetParent(root.transform, false);
            SmartLineRenderer lineRend = lineObj.AddComponent<SmartLineRenderer>();
            lineRend.StartAnchor = startAnchor;
            lineRend.EndAnchor = endAnchor;

            // 5. Sample Obstacle Box with SmartSnapTarget
            GameObject obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            obstacle.name = "Obstacle_Node";
            obstacle.transform.SetParent(root.transform, false);
            obstacle.transform.position = new Vector3(0f, 0f, 0f);
            obstacle.transform.localScale = new Vector3(1.5f, 1.5f, 1f);
            Object.DestroyImmediate(obstacle.GetComponent<Collider>());
            obstacle.AddComponent<BoxCollider2D>();
            obstacle.AddComponent<SmartSnapTarget>();

            Selection.activeGameObject = lineObj;
        }
    }
}
#endif
