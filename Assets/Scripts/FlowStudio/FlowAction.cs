using System;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WhiteWhale.FlowStudio
{
    public abstract class FlowAction : ScriptableObject
    {
        public virtual string SubjectLabel => null;
        public virtual string TargetLabel => null;
        public virtual bool AcceptsSubject(GameObject subject) => subject != null;
        public virtual bool AcceptsTarget(GameObject target) => target != null;
        public abstract FlowActionRun Begin(FlowActionContext context, GameObject subject, GameObject target);
    }

    public abstract class FlowActionRun
    {
        public abstract bool? Poll();
        public abstract void Stop();
    }

    public sealed class FlowActionContext
    {
        public GameObject Host { get; }
        public Scene Scene => Host.scene;
        public FlowActionContext(GameObject host) => Host = host;
    }
}
