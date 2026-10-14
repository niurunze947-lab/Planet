using System;
using System.Collections.Generic;
using Planet.Procedural.Terrain;
using Planet.Solar;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Yongxian.PlanetSculpt
{
    // Displays the SAME edited globe through the original metre camera. The original
    // double-precision navigation remains authoritative, including scroll and flight.
    public sealed class SculptFlightBridge : MonoBehaviour, IPlanetCollisionSurfaceQuery
    {
        PlanetTerrain terrain;
        SolarSystemSimulation simulation;
        CelestialBody body;
        int bodyIndex, revision = -1;
        double radius;
        double3 eyeBf;
        Vector3[] sourceVertices, projectedVertices;
        int[] indices;
        Scene queryScene;
        MeshCollider queryCollider;
        GameObject localObject;
        Mesh localMesh;
        MeshRenderer localRenderer;
        Material localMaterial;
        public bool Near { get; private set; }
        public double ClearanceMetres { get; private set; }
        public bool CollisionCoverageReady => queryCollider != null;
        public double RadiusMetres => radius;
        public double3 EyeBf => eyeBf;

        public void Initialize(PlanetTerrain target, CelestialBody earth)
        {
            terrain = target; body = earth; simulation = SolarSystemSimulation.Instance;
            radius = earth.Definition.RadiusKm * 1000.0;
            for (int i = 0; i < simulation.BodyCount; i++) if (simulation.ViewAt(i) == body) bodyIndex = i;
            queryScene = SceneManager.CreateScene("Sculpt collision " + GetInstanceID(), new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            var proxy = new GameObject("Unit sculpt collision");
            SceneManager.MoveGameObjectToScene(proxy, queryScene);
            queryCollider = proxy.AddComponent<MeshCollider>();
            localObject = new GameObject("Edited terrain - metre projection");
            localObject.layer = ObserverRenderRig.TerrainLayer;
            localObject.hideFlags = HideFlags.DontSave;
            localMesh = new Mesh { name = "Edited terrain relative to observer", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            localMesh.MarkDynamic();
            localObject.AddComponent<MeshFilter>().sharedMesh = localMesh;
            localRenderer = localObject.AddComponent<MeshRenderer>();
            localMaterial = new Material(terrain.GetComponent<MeshRenderer>().sharedMaterial);
            localMaterial.SetFloat("_SculptLocalProjection", 1f);
            localMaterial.SetFloat("_PlanetRadius", (float)radius);
            localRenderer.sharedMaterial = localMaterial;
            localRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            localRenderer.receiveShadows = false;
            RefreshGeometry();
        }
        void RefreshGeometry()
        {
            if (revision == terrain.Revision) return;
            revision = terrain.Revision;
            sourceVertices = terrain.SurfaceMesh.vertices;
            indices = terrain.SurfaceMesh.triangles;
            projectedVertices = new Vector3[sourceVertices.Length];
            queryCollider.sharedMesh = null;
            queryCollider.sharedMesh = terrain.SurfaceMesh;
            localMesh.Clear();
            localMesh.vertices = sourceVertices;
            localMesh.triangles = indices;
            localMesh.normals = terrain.SurfaceMesh.normals;
            localMesh.uv2 = terrain.SurfaceMesh.uv2;
            localMesh.SetUVs(2, new List<Vector3>(sourceVertices));
        }
        public void Sync()
        {
            RefreshGeometry();
            Vec3d p = simulation.ObserverRelativeToBody(bodyIndex);
            eyeBf = new double3(p.x, p.y, p.z);
            // Only nearby geometry needs the original local/metre rendering path.
            ClearanceMetres = math.length(eyeBf) - SampleCollisionRadiusMetres(eyeBf);
            Near = simulation.HasObserver && ClearanceMetres < 3500;
            localRenderer.enabled = Near;
            terrain.GetComponent<MeshRenderer>().enabled = true;
            if (!Near) return;
            double3 phase = eyeBf - math.floor(eyeBf / 1024.0) * 1024.0;
            localMaterial.SetVector("_SculptRockOrigin", new Vector4((float)phase.x, (float)phase.y, (float)phase.z, 0));
            for (int i = 0; i < sourceVertices.Length; i++)
                projectedVertices[i] = (Vector3)(float3)((double3)(float3)sourceVertices[i] * radius - eyeBf);
            localMesh.vertices = projectedVertices;
            localMesh.RecalculateBounds();
            localObject.transform.SetPositionAndRotation(Vector3.zero, body.transform.rotation);
        }
        bool Hit(double3 origin, double3 direction, out RaycastHit hit, out double3 precise)
        {
            RefreshGeometry();
            precise = double3.zero;
            direction = math.normalizesafe(direction);
            // Cast only within the body's envelope, even when the observer is far away.
            double b = math.dot(origin, direction);
            double c = math.lengthsq(origin) - 4.0;
            double disc = b * b - c;
            if (disc < 0) { hit = default; return false; }
            double advance = math.max(0, -b - math.sqrt(disc));
            double3 start = origin + direction * advance;
            var ray = new Ray((Vector3)(float3)start, (Vector3)(float3)direction);
            if (!queryCollider.Raycast(ray, out hit, 4f)) return false;
            int t = hit.triangleIndex * 3;
            if (t < 0 || t + 2 >= indices.Length) return false;
            // Refine the selected triangle in double precision so a 0.5 metre
            // navigation clearance does not jitter with unit-mesh float hit points.
            double3 a = (double3)(float3)sourceVertices[indices[t]];
            double3 edge1 = (double3)(float3)sourceVertices[indices[t + 1]] - a;
            double3 edge2 = (double3)(float3)sourceVertices[indices[t + 2]] - a;
            double3 n = math.cross(edge1, edge2);
            double denominator = math.dot(n, direction);
            if (Math.Abs(denominator) < 1e-18) return false;
            precise = start + direction * (math.dot(n, a - start) / denominator);
            return true;
        }
        public bool Pointer(Camera camera, Vector3 mouse, out RaycastHit hit, out Vector3 localPoint)
        {
            Rect r = camera.pixelRect;
            float x = (mouse.x - r.x) / Mathf.Max(1f, r.width) * 2f - 1f;
            float y = (mouse.y - r.y) / Mathf.Max(1f, r.height) * 2f - 1f;
            float t = Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * 0.5f);
            Vector3 world = camera.transform.TransformDirection(new Vector3(x * camera.aspect * t, y * t, 1));
            Vec3d p = simulation.ObserverRelativeToBody(bodyIndex);
            bool ok = Hit(new double3(p.x, p.y, p.z) / radius,
                (double3)(float3)(Quaternion.Inverse(body.transform.rotation) * world), out hit, out var point);
            localPoint = (Vector3)(float3)point;
            return ok;
        }
        public bool SamplePoint(Vector3 direction, out Vector3 point)
        {
            double3 d = math.normalizesafe((double3)(float3)direction);
            bool ok = Hit(d * 2, -d, out _, out var precise);
            point = (Vector3)(float3)precise;
            return ok;
        }
        public Vector3 DisplayPoint(Vector3 point)
        {
            return Near ? body.transform.rotation * (Vector3)(float3)((double3)(float3)point * radius - eyeBf)
                : terrain.transform.TransformPoint(point);
        }
        public double SampleCollisionRadiusMetres(double3 directionBf)
        {
            double3 d = math.normalizesafe(directionBf, new double3(0, 1, 0));
            return Hit(d * 2, -d, out _, out var p) ? math.length(p) * radius : radius;
        }
        public bool SweepSphere(double3 from, double3 to, double size, out double3 resolved, out double3 normal)
        {
            return TerrainSurfaceSweep.Sweep(from, to, size, radius * 1.5, SampleCollisionRadiusMetres, out resolved, out normal);
        }
        public void Shutdown()
        {
            if (localObject != null) Destroy(localObject);
            if (localMesh != null) Destroy(localMesh);
            if (localMaterial != null) Destroy(localMaterial);
            if (queryScene.IsValid() && queryScene.isLoaded) SceneManager.UnloadSceneAsync(queryScene);
            queryScene = default;
            localObject = null; localMesh = null; localMaterial = null;
        }
        void OnDestroy() { Shutdown(); }
    }
}
