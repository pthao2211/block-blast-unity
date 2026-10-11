using System;
using System.Collections.Generic;
using UnityEngine;

namespace BlockBlast
{
    [Serializable]
    public class BlastState
    {
        public int version = 1;
        public int[] cells = new int[64];
        public int[] tray = new int[3];
        public int[] colors = new int[3];
        public int score, combo, misses, moves, level = 1;
        public bool adventure, ended, won;
        public int hammer = 1, bomb = 1, shuffle = 1;
    }

    public sealed class BlastModel
    {
        public static readonly Vector2Int[][] Shapes = MakeShapes();
        public BlastState State { get; private set; }
        readonly System.Random random;
        public int Target { get { return 500 + State.level * 150; } }
        public BlastModel(int seed) { random = new System.Random(seed); NewGame(false, 1); }
        public void NewGame(bool adventure, int level)
        {
            State = new BlastState { adventure = adventure, level = Mathf.Clamp(level, 1, 99), moves = 30 };
            if (adventure)
            {
                // Opening obstacles leave the upper half open and never start with full lines.
                for (int y = 5; y < 8; y++) for (int x = 0; x < 8; x++)
                    if (random.Next(100) < Mathf.Min(60, 18 + level * 3)) State.cells[y * 8 + x] = random.Next(1, 9);
            }
            Deal();
        }
        public bool Restore(BlastState state)
        {
            if (state == null || state.version != 1 || state.cells == null || state.cells.Length != 64 ||
                state.tray == null || state.tray.Length != 3 || state.colors == null || state.colors.Length != 3) return false;
            foreach (int c in state.cells) if (c < 0 || c > 8) return false;
            for (int i = 0; i < 3; i++) if (state.tray[i] < -1 || state.tray[i] >= Shapes.Length || state.colors[i] < 1 || state.colors[i] > 8) return false;
            if (state.score < 0 || state.moves < 0 || state.level < 1 || state.level > 99 || state.combo < 0 || state.misses < 0 || state.misses > 2 ||
                state.hammer < 0 || state.hammer > 1 || state.bomb < 0 || state.bomb > 1 || state.shuffle < 0 || state.shuffle > 1) return false;
            State = state;
            return true;
        }
        public bool CanPlace(int shape, int x, int y)
        {
            if (shape < 0 || shape >= Shapes.Length) return false;
            foreach (var p in Shapes[shape])
            {
                int px = x + p.x, py = y + p.y;
                if (px < 0 || px > 7 || py < 0 || py > 7 || State.cells[py * 8 + px] != 0) return false;
            }
            return true;
        }
        public bool HasFit(int shape)
        {
            for (int y = 0; y < 8; y++) for (int x = 0; x < 8; x++) if (CanPlace(shape, x, y)) return true;
            return false;
        }
        public bool AnyMove()
        {
            foreach (int shape in State.tray) if (HasFit(shape)) return true;
            return false;
        }
        public List<int> PreviewLines(int shape, int x, int y)
        {
            if (!CanPlace(shape, x, y)) return new List<int>();
            int[] copy = (int[])State.cells.Clone();
            foreach (var p in Shapes[shape]) copy[(y + p.y) * 8 + x + p.x] = 1;
            return FullCells(copy);
        }
        public bool Place(int slot, int x, int y, out List<int> cleared, out int lines)
        {
            cleared = new List<int>(); lines = 0;
            if (State.ended || slot < 0 || slot > 2 || !CanPlace(State.tray[slot], x, y)) return false;
            int shape = State.tray[slot];
            foreach (var p in Shapes[shape]) State.cells[(y + p.y) * 8 + x + p.x] = State.colors[slot];
            State.score += Shapes[shape].Length * 10;
            State.tray[slot] = -1;
            for (int i = 0; i < 8; i++)
            {
                bool row = true, col = true;
                for (int j = 0; j < 8; j++) { row &= State.cells[i * 8 + j] != 0; col &= State.cells[j * 8 + i] != 0; }
                if (row) lines++; if (col) lines++;
            }
            cleared = FullCells(State.cells);
            if (lines > 0)
            {
                State.combo++; State.misses = 0;
                State.score += 80 * lines * lines * Mathf.Min(State.combo, 10);
                foreach (int index in cleared) State.cells[index] = 0;
                bool empty = true; foreach (int c in State.cells) empty &= c == 0;
                if (empty) State.score += 300;
            }
            else if (++State.misses == 3) { State.combo = 0; State.misses = 0; }
            if (State.adventure) State.moves--;
            if (State.tray[0] == -1 && State.tray[1] == -1 && State.tray[2] == -1) Deal();
            Evaluate();
            return true;
        }
        public void Evaluate()
        {
            State.won = State.adventure && State.score >= Target;
            State.ended = State.won || (State.adventure && State.moves <= 0) || !AnyMove();
        }
        public List<int> Erase(int x, int y, bool bomb)
        {
            var removed = new List<int>();
            if (State.won || (State.adventure && State.moves <= 0) || x < 0 || x > 7 || y < 0 || y > 7 ||
                (bomb ? State.bomb : State.hammer) == 0) return removed;
            int radius = bomb ? 1 : 0;
            for (int py = Math.Max(0, y - radius); py <= Math.Min(7, y + radius); py++)
                for (int px = Math.Max(0, x - radius); px <= Math.Min(7, x + radius); px++)
                    if (State.cells[py * 8 + px] != 0) removed.Add(py * 8 + px);
            if (removed.Count == 0) return removed;
            foreach (int index in removed) State.cells[index] = 0;
            if (bomb) State.bomb--; else State.hammer--;
            Evaluate(); return removed;
        }
        public bool Shuffle()
        {
            if (State.shuffle == 0 || State.won || (State.adventure && State.moves <= 0)) return false;
            State.shuffle--;
            Deal(); Evaluate(); return true;
        }
        void Deal()
        {
            for (int i = 0; i < 3; i++) { State.tray[i] = random.Next(Shapes.Length); State.colors[i] = random.Next(1, 9); }
        }
        static List<int> FullCells(int[] cells)
        {
            var result = new List<int>(); var marked = new bool[64];
            for (int i = 0; i < 8; i++)
            {
                bool row = true, col = true;
                for (int j = 0; j < 8; j++) { row &= cells[i * 8 + j] != 0; col &= cells[j * 8 + i] != 0; }
                if (row) for (int j = 0; j < 8; j++) marked[i * 8 + j] = true;
                if (col) for (int j = 0; j < 8; j++) marked[j * 8 + i] = true;
            }
            for (int i = 0; i < 64; i++) if (marked[i]) result.Add(i);
            return result;
        }
        static Vector2Int[][] MakeShapes()
        {
            var list = new List<Vector2Int[]>();
            for (int length = 1; length <= 5; length++)
            {
                var a = new Vector2Int[length]; var b = new Vector2Int[length];
                for (int i = 0; i < length; i++) { a[i] = new Vector2Int(i, 0); b[i] = new Vector2Int(0, i); }
                list.Add(a); if (length > 1) list.Add(b);
            }
            foreach (var size in new[] { new Vector2Int(2,2), new Vector2Int(3,2), new Vector2Int(2,3), new Vector2Int(3,3) })
            {
                var a = new List<Vector2Int>(); for (int y = 0; y < size.y; y++) for (int x = 0; x < size.x; x++) a.Add(new Vector2Int(x,y)); list.Add(a.ToArray());
            }
            var bases = new[] {
                new[] {new Vector2Int(0,0),new Vector2Int(0,1),new Vector2Int(1,1)},
                new[] {new Vector2Int(0,0),new Vector2Int(0,1),new Vector2Int(0,2),new Vector2Int(1,2)},
                new[] {new Vector2Int(0,0),new Vector2Int(1,0),new Vector2Int(2,0),new Vector2Int(1,1)},
                new[] {new Vector2Int(1,0),new Vector2Int(2,0),new Vector2Int(0,1),new Vector2Int(1,1)}
            };
            foreach (var shape in bases) for (int rotation = 0; rotation < 4; rotation++)
            {
                var a = new Vector2Int[shape.Length]; int minX = 100, minY = 100;
                for (int i = 0; i < shape.Length; i++)
                {
                    var p = shape[i]; for (int j = 0; j < rotation; j++) p = new Vector2Int(-p.y,p.x);
                    a[i] = p; minX = Math.Min(minX,p.x); minY = Math.Min(minY,p.y);
                }
                for (int i = 0; i < a.Length; i++) a[i] -= new Vector2Int(minX,minY);
                list.Add(a);
            }
            return list.ToArray();
        }
    }
}
