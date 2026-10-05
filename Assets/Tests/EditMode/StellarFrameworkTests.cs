using System;
using System.Collections.Generic;
using NUnit.Framework;
using Planet.Procedural;
using UnityEngine;

public sealed class StellarFrameworkTests
{
    [Test]
    public void StarsAndPlanetsSharePhysicalAuthorityThroughTheCelestialBase()
    {
        var planet = ScriptableObject.CreateInstance<PlanetDefinition>();
        var star = ScriptableObject.CreateInstance<StellarDefinition>();
        try
        {
            Assert.IsInstanceOf<CelestialDefinition>(planet); Assert.IsInstanceOf<CelestialDefinition>(star);
            star.physical.radiusMetres = 696340000;
            star.physical.massKilograms = 1.98847e30;
            Assert.That(star.physical.SurfaceGravity, Is.InRange(273, 275));
            Assert.IsFalse(typeof(PlanetDefinition).IsAssignableFrom(star.GetType()));
        }
        finally { UnityEngine.Object.DestroyImmediate(planet); UnityEngine.Object.DestroyImmediate(star); }
    }

    [Test]
    public void IntegratedPlanckRadianceMatchesStefanBoltzmannLaw()
    {
        const double temperature = 5778;
        double minLog = Math.Log(1e-8), maxLog = Math.Log(1e-3), step = (maxLog-minLog)/4000;
        double integrated = 0;
        for (int i=0;i<4000;i++)
        {
            double wavelength = Math.Exp(minLog+(i+0.5)*step);
            integrated += StellarRadiation.SpectralRadiance(wavelength,temperature)*wavelength*step;
        }
        Assert.That(integrated*Math.PI/StellarRadiation.SurfaceFlux(temperature), Is.EqualTo(1).Within(0.0001));
        Assert.That(StellarRadiation.SurfaceFlux(temperature), Is.InRange(6.2e7,6.4e7));
    }

    [Test]
    public void HotterBlackbodyHasMoreBlueAndUmbraHasMuchLowerRadiance()
    {
        Color cold=StellarRadiation.LinearRgb(3800),sun=StellarRadiation.LinearRgb(5778),hot=StellarRadiation.LinearRgb(8000);
        Assert.That(cold.b/cold.r, Is.LessThan(sun.b/sun.r));
        Assert.That(hot.b/hot.r, Is.GreaterThan(sun.b/sun.r));
        Assert.That(StellarRadiation.SurfaceFlux(3800)/StellarRadiation.SurfaceFlux(5778), Is.InRange(0.18,0.20));
        Assert.That(sun.r*0.2126+sun.g*0.7152+sun.b*0.0722, Is.EqualTo(1).Within(0.001));
    }

    [Test]
    public void DifferentialRotationAndQuadraticLimbLawUsePhysicalParameters()
    {
        var star=ScriptableObject.CreateInstance<StellarDefinition>();
        try
        {
            Assert.That(StellarRadiation.RotationPeriodDays(star,0),Is.EqualTo(27).Within(0.0001));
            Assert.That(StellarRadiation.RotationPeriodDays(star,Math.PI/2),Is.EqualTo(37).Within(0.0001));
            Assert.That(StellarRadiation.RotationPeriodDays(star,Math.PI/4),Is.InRange(27,37));
            Assert.AreEqual(1,StellarRadiation.LimbIntensity(star,1));
            Assert.That(StellarRadiation.LimbIntensity(star,0),Is.EqualTo(0.34).Within(0.00001));
            Assert.That(StellarRadiation.LimbIntensity(star,0.5f),Is.GreaterThan(StellarRadiation.LimbIntensity(star,0)));
        }
        finally { UnityEngine.Object.DestroyImmediate(star); }
    }

    [TestCase(0)] [TestCase(3)] [TestCase(6)]
    public void IcosphereIsClosedOutwardUnitGeometryWithNoUvPoleDistortion(int subdivisions)
    {
        Mesh mesh=StellarIcosphere.Get(subdivisions);
        Assert.AreEqual(10*(int)Math.Pow(4,subdivisions)+2,mesh.vertexCount);
        Assert.AreEqual(20*(int)Math.Pow(4,subdivisions),mesh.triangles.Length/3);
        Assert.AreEqual(0,mesh.uv.Length);
        Vector3[] vertices=mesh.vertices; int[] triangles=mesh.triangles;
        var edges=new Dictionary<long,int>();
        foreach(Vector3 v in vertices) Assert.That(v.magnitude,Is.EqualTo(1).Within(0.000001));
        for(int i=0;i<triangles.Length;i+=3)
        {
            int a=triangles[i],b=triangles[i+1],c=triangles[i+2];
            Assert.That(Vector3.Dot(Vector3.Cross(vertices[b]-vertices[a],vertices[c]-vertices[a]),vertices[a]),Is.GreaterThan(0));
            CountEdge(edges,a,b); CountEdge(edges,b,c); CountEdge(edges,c,a);
        }
        foreach(int neighbours in edges.Values) Assert.AreEqual(2,neighbours);
    }
    private static void CountEdge(Dictionary<long,int> edges,int a,int b)
    {
        long key=((long)Mathf.Min(a,b)<<32)|(uint)Mathf.Max(a,b);
        edges[key]=edges.ContainsKey(key)?edges[key]+1:1;
    }
}
