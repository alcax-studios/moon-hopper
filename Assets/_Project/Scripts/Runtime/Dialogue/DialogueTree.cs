using System;
using System.Collections.Generic;
using UnityEngine;

namespace Alcax.Dialogue
{
    /// <summary>
    /// A branching conversation: nodes of NPC lines, each offering player choices that lead to
    /// another node or end the conversation. Authored as data (see AtriumBuilder for the info desk).
    /// </summary>
    [CreateAssetMenu(menuName = "Alcax/Dialogue Tree", fileName = "Dialogue")]
    public class DialogueTree : ScriptableObject
    {
        [Serializable]
        public class Choice
        {
            public string text;
            [Tooltip("Node id to go to. Leave empty to end the conversation.")]
            public string next;
        }

        [Serializable]
        public class Node
        {
            public string id;
            [TextArea(2, 5)] public string line;
            public List<Choice> choices = new();
        }

        public string startNode = "start";
        public List<Node> nodes = new();

        public Node Get(string id) => nodes.Find(n => n.id == id);

        public Node Add(string id, string line, params (string text, string next)[] choices)
        {
            var n = new Node { id = id, line = line };
            foreach (var (t, nx) in choices) n.choices.Add(new Choice { text = t, next = nx });
            nodes.Add(n);
            return n;
        }
    }
}
