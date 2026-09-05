using NUnit.Framework;
using UnityEngine;
using MCPForUnity.Editor.Tools.Animation;
using static MCPForUnityTests.Editor.TestUtilities;

namespace MCPForUnityTests.Editor.Tools
{
    /// <summary>
    /// Direct coverage of the resolver every animator read and control response depends on,
    /// including the Play-mode parameter branches that an EditMode test cannot drive.
    /// </summary>
    public class AnimatorResolverTests
    {
        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
                UnityEngine.Object.DestroyImmediate(_root);
        }

        private GameObject Child(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root.transform);
            return go;
        }

        [Test]
        public void Find_NullTarget_ReturnsNullAndEmptyCandidates()
        {
            var resolved = AnimatorResolver.Find(null, out var candidates);
            Assert.IsNull(resolved);
            Assert.IsNotNull(candidates, "candidates must never be null - callers pass it straight on");
            Assert.AreEqual(0, candidates.Length);
        }

        [Test]
        public void Find_NoAnimatorAnywhere_ReturnsNullAndEmptyCandidates()
        {
            _root = new GameObject("AnimResTest_Empty");
            Child("AnimResTest_EmptyChild");

            var resolved = AnimatorResolver.Find(_root, out var candidates);
            Assert.IsNull(resolved);
            Assert.AreEqual(0, candidates.Length);
        }

        [Test]
        public void Find_AnimatorOnTarget_PrefersItOverDescendants()
        {
            _root = new GameObject("AnimResTest_Priority");
            var own = _root.AddComponent<Animator>();
            Child("AnimResTest_PriorityChild").AddComponent<Animator>();

            var resolved = AnimatorResolver.Find(_root, out var candidates);
            Assert.AreSame(own, resolved, "An Animator on the target wins over any descendant");
            Assert.AreEqual(0, candidates.Length, "The exact-target path reports no candidates");
        }

        [Test]
        public void Find_SingleDescendantAnimator_ResolvesIt()
        {
            _root = new GameObject("AnimResTest_Single");
            var rig = Child("AnimResTest_SingleRig").AddComponent<Animator>();

            Assert.AreSame(rig, AnimatorResolver.Find(_root, out _));
        }

        [Test]
        public void Find_InactiveDescendantAnimator_ResolvesIt()
        {
            _root = new GameObject("AnimResTest_Inactive");
            var child = Child("AnimResTest_InactiveRig");
            var rig = child.AddComponent<Animator>();
            child.SetActive(false);

            Assert.AreSame(rig, AnimatorResolver.Find(_root, out _),
                "A disabled rig is still readable");
        }

        [Test]
        public void Find_SeveralDescendantAnimators_ReturnsNullAndReportsThem()
        {
            _root = new GameObject("AnimResTest_Ambiguous");
            Child("AnimResTest_RigA").AddComponent<Animator>();
            Child("AnimResTest_RigB").AddComponent<Animator>();

            var resolved = AnimatorResolver.Find(_root, out var candidates);
            Assert.IsNull(resolved, "An ambiguous request must not pick one silently");
            Assert.AreEqual(2, candidates.Length);
        }

        [Test]
        public void NotResolvedError_Ambiguous_NamesEveryCandidate()
        {
            _root = new GameObject("AnimResTest_ErrAmbiguous");
            Child("AnimResTest_ErrRigA").AddComponent<Animator>();
            Child("AnimResTest_ErrRigB").AddComponent<Animator>();

            AnimatorResolver.Find(_root, out var candidates);
            var error = ToJObject(AnimatorResolver.NotResolvedError(_root, candidates));

            Assert.IsFalse(error.Value<bool>("success"));
            string message = error["message"].ToString();
            StringAssert.Contains("AnimResTest_ErrRigA", message);
            StringAssert.Contains("AnimResTest_ErrRigB", message);
        }

        [Test]
        public void NotResolvedError_NoCandidates_ReportsTargetAndChildren()
        {
            _root = new GameObject("AnimResTest_ErrEmpty");

            AnimatorResolver.Find(_root, out var candidates);
            var error = ToJObject(AnimatorResolver.NotResolvedError(_root, candidates));

            Assert.IsFalse(error.Value<bool>("success"));
            StringAssert.Contains("or its children", error["message"].ToString());
        }

        [Test]
        public void ResolvedSuffix_TargetCarriesTheAnimator_IsEmpty()
        {
            _root = new GameObject("AnimResTest_SuffixSame");
            var own = _root.AddComponent<Animator>();

            Assert.AreEqual(string.Empty, AnimatorResolver.ResolvedSuffix(_root, own),
                "A response that did not retarget must read exactly as before");
        }

        [Test]
        public void ResolvedSuffix_Retargeted_NamesBothObjects()
        {
            _root = new GameObject("AnimResTest_SuffixRoot");
            var rig = Child("AnimResTest_SuffixRig").AddComponent<Animator>();

            string suffix = AnimatorResolver.ResolvedSuffix(_root, rig);
            StringAssert.Contains("AnimResTest_SuffixRig", suffix);
            StringAssert.Contains("AnimResTest_SuffixRoot", suffix);
        }

        [Test]
        public void Describe_TargetCarriesTheAnimator_NamesItOnce()
        {
            _root = new GameObject("AnimResTest_DescSame");
            var own = _root.AddComponent<Animator>();

            Assert.AreEqual("'AnimResTest_DescSame'", AnimatorResolver.Describe(_root, own));
        }

        [Test]
        public void Describe_Retargeted_NamesBothObjects()
        {
            _root = new GameObject("AnimResTest_DescRoot");
            var rig = Child("AnimResTest_DescRig").AddComponent<Animator>();

            string described = AnimatorResolver.Describe(_root, rig);
            StringAssert.Contains("AnimResTest_DescRig", described);
            StringAssert.Contains("AnimResTest_DescRoot", described);
        }
    }
}
