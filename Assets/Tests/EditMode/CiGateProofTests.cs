using NUnit.Framework;

public class CiGateProofTests
{
    [Test]
    public void DeliberatelyFailsToProveTheGate()
    {
        Assert.Fail("CI gate proof: this failure should block the merge.");
    }
}
