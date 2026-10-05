using NUnit.Framework;
using Planet.Procedural;
using UnityEngine;

public sealed class PlanetTextureLodTests
{
    private PlanetTextureLodSet set;
    [SetUp] public void SetUp() {set=ScriptableObject.CreateInstance<PlanetTextureLodSet>();set.tiers=new PlanetTextureTier[4];}
    [TearDown] public void TearDown() {Object.DestroyImmediate(set);}
    [TestCase(8,0)] [TestCase(3,1)] [TestCase(0.7f,2)] [TestCase(0.1f,3)]
    public void PublishedSurfaceClearanceSelectsResolutionWithoutChangingCamera(float ratio,int expected)
    {
        Assert.AreEqual(expected,set.SelectTier(ratio,true,0));
    }
    [Test] public void NearSurfaceHysteresisDoesNotOscillateOrShareInteractionState()
    {
        Assert.AreEqual(3,set.SelectTier(0.25f,true,2));
        Assert.AreEqual(3,set.SelectTier(0.30f,true,3));
        Assert.AreEqual(2,set.SelectTier(0.30f,true,2));
        Assert.AreEqual(2,set.SelectTier(0.35f,true,3));
        Assert.AreEqual(2,set.SelectTier(1.10f,true,2));
        Assert.AreEqual(1,set.SelectTier(1.20f,true,2));
    }
    [Test] public void LeavingEarthOrInvalidCameraStateReleasesHighDetailSelection()
    {
        Assert.AreEqual(0,set.SelectTier(0.01f,false,3));
        Assert.AreEqual(0,set.SelectTier(float.NaN,true,3));
        Assert.AreEqual(0,set.SelectTier(float.PositiveInfinity,true,3));
        Assert.AreEqual(0,set.SelectTier(100f,true,3));
    }
    [Test] public void BothDisplayScalesChooseTheSameRatioTier()
    {
        foreach(float radius in new [] {0.006371f,2.5484f})
        {
            float viewHeight=radius*1.20f;
            float ratio=(viewHeight-radius)/radius;
            Assert.AreEqual(3,set.SelectTier(ratio,true,0));
        }
    }

    [Test] public void MeshTierUsesTheSameFocusAndClearanceHysteresisAsTextureTier()
    {
        var meshSet = ScriptableObject.CreateInstance<PlanetMeshLodSet>();
        meshSet.tiers = new PlanetMeshTier[4];
        try
        {
            Assert.AreEqual(0, meshSet.SelectTier(8f, false, 3));
            Assert.AreEqual(0, meshSet.SelectTier(8f, true, 3));
            Assert.AreEqual(1, meshSet.SelectTier(3f, true, 0));
            Assert.AreEqual(2, meshSet.SelectTier(0.7f, true, 1));
            Assert.AreEqual(3, meshSet.SelectTier(0.1f, true, 2));
            Assert.AreEqual(3, meshSet.SelectTier(0.30f, true, 3));
        }
        finally { Object.DestroyImmediate(meshSet); }
    }
}
