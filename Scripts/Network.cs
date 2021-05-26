using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Coherence
{
    public enum NetworkTargetType
    {
        Object = 0,
        Component,
        Viewport,
        Image,
        Mesh,
    }

    public interface INetworkTarget
    {
        void OnRegistered();
        void OnUnregistered();

        NetworkTargetType GetNetworkType();
        string GetNetworkName();
    }

    // New idea - generic Coherence network interface. Statically available.
    public class Network
    {
        public delegate void MessageHandler<T>(string target, T handler);

        public delegate void RequestHandler(IntPtr ptr, InteropMessageHeader header);

        internal static event OnCoherenceEvent OnCoherenceConnected;
        internal static event OnCoherenceEvent OnCoherenceDisconnected;
        internal static event OnCoherenceEvent OnCoherenceEnabled;
        internal static event OnCoherenceEvent OnCoherenceDisabled;

        //public static void Register<T>(RpcRequest request, MessageHandler<T> handler) where T : struct
       // {
            // would be nice but I'd have to invoke ptrtostruct T and can't have a list of these...
            // unless I'm making a dynamic wrapper invoked for each one? but UNREGISTERING? Holy balls no
       // }

        /// <summary>
        /// All registered INetworkTarget instances organized into a lookup table by type + name
        /// </summary>
        static Dictionary<NetworkTargetType, Dictionary<string, INetworkTarget>> targets =
            new Dictionary<NetworkTargetType, Dictionary<string, INetworkTarget>>();

        /// <summary>
        /// All registered network event handlers for targets.
        /// </summary>
        static Dictionary<INetworkTarget, Dictionary<RpcRequest, RequestHandler>> targetHandlers
            = new Dictionary<INetworkTarget, Dictionary<RpcRequest, RequestHandler>>();

        /// <summary>
        /// All registered network event handlers for plugins.
        ///
        /// These are one-to-one (an event may only be handled by one plugin)
        /// </summary>
        static Dictionary<RpcRequest, RequestHandler> handlers
            = new Dictionary<RpcRequest, RequestHandler>();

        public static void Register(RpcRequest request, RequestHandler handler)
        {
            if (handlers.ContainsKey(request))
            {
                throw new Exception($"There is already a handler registered to '{request}'");
            }

            handlers.Add(request, handler);
        }

        public static void Register(INetworkTarget target, RpcRequest request, RequestHandler handler)
        {
            if (!targetHandlers.ContainsKey(target))
            {
                throw new Exception(
                    $"Instance was not added through Network.RegisterTarget() first"
                );
            }

            targetHandlers[target].Add(request, handler);
        }

        public static void RegisterTarget(INetworkTarget target)
        {
            if (targetHandlers.ContainsKey(target))
            {
                throw new Exception(
                    $"Already registered {target.GetNetworkType()} : {target.GetNetworkName()}"
                );
            }

            targetHandlers[target] = new Dictionary<RpcRequest, RequestHandler>();

            if (targets.TryGetValue(target.GetNetworkType(), out var instances))
            {
                instances.Add(target.GetNetworkName(), target);
            }
            else
            {
                targets[target.GetNetworkType()] = new Dictionary<string, INetworkTarget>
                {
                    [target.GetNetworkName()] = target
                };
            }

            target.OnRegistered();
        }

        public static void UnregisterTarget(INetworkTarget target)
        {
            targetHandlers.Remove(target);
            targets[target.GetNetworkType()].Remove(target.GetNetworkName());

            target.OnUnregistered();
        }

        public static IEnumerable<INetworkTarget> GetTargets(NetworkTargetType type)
        {
            return targets[type].Values;
        }

        public static INetworkTarget FindTarget(NetworkTargetType type, string name)
        {
            if (!targets.ContainsKey(type))
            {
                return null;
            }

            if (!targets[type].ContainsKey(name))
            {
                return null;
            }

            return targets[type][name];
        }

        internal static void RouteInboundMessage(NetworkTargetType targetType, string targetName, IntPtr ptr, InteropMessageHeader header)
        {
            // Invoke a plugin handler if we have one registered
            if (handlers.ContainsKey(header.type))
            {
                handlers[header.type](ptr, header);
                return;
            }

            // Otherwise - lookup a target by type + name and route that way.
            var target = FindTarget(targetType, targetName);
            if (target == null)
            {
                throw new Exception($"Unknown target {targetType} : {targetName}");
            }

            if (targetHandlers[target].TryGetValue(header.type, out var handler))
            {
                handler(ptr, header);
            }
            else
            {
                throw new Exception($"Target has no handler for inbound request {header.type}");
            }
        }

        internal static void RemoveAllHandlers()
        {
            handlers.Clear();
            targets.Clear();
            targetHandlers.Clear();
        }

        // and outbound message handlers here.
    }
}
