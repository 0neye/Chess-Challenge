using ChessChallenge.API;
using System;
using System.Linq;

public class MyBot : IChessBot
{
    // Piece values: null, pawn, knight, bishop, rook, queen, king
    readonly int[] PIECE_VALS = { 0, 100, 300, 320, 500, 900, 10000 };

    const int MAX_DEPTH = 100;
    const int INF = 20000000;
    ulong nodes = 0; //debug
    int maxPly = 0; //debug
    Move bestMove = new();

    struct TTEntry
    {
        public ulong key;
        public Move move;
        public int depth, score, flag;
    }

    const int NUM_ENTRIES = 1 << 23;
    readonly TTEntry[] TTable = new TTEntry[NUM_ENTRIES];
    readonly long[,] history = new long[64, 64];

    public Move Think(Board board, Timer timer)
    {
        int bestScore = -INF;
        for (int depth = 1; depth <= MAX_DEPTH; depth++)
        {
            nodes = 0;
            maxPly = 0;
            bestScore = Math.Max(bestScore, Search(board, depth, -INF, INF, 0, timer));

            if (timer.MillisecondsElapsedThisTurn * 300 > timer.MillisecondsRemaining) break;
            Console.WriteLine($"Mybot depth {depth}; score {bestScore}; time {timer.MillisecondsElapsedThisTurn}; pv {bestMove}; nodes {nodes}; nps {nodes * 1000 / ((ulong)timer.MillisecondsElapsedThisTurn + 1)}; max ply {maxPly}");
        }
        Console.WriteLine();
        return bestMove;
    }

    int Search(Board board, int depth, int alpha, int beta, int ply, Timer timer)
    {
        nodes++;
        maxPly = Math.Max(maxPly, ply);
        ulong key = board.ZobristKey;
        bool isQsearch = depth <= 0, inCheck = board.IsInCheck();
        int bestEval = -INF;
        Move bestMove = new();

        if (ply > 0 && board.IsRepeatedPosition())
            return -2000;

        if (timer.MillisecondsElapsedThisTurn * 300 > timer.MillisecondsRemaining)
            return 0;

        TTEntry entry = TTable[key % NUM_ENTRIES];

        if (ply > 0 && entry.key == key && entry.depth >= depth &&
        (entry.flag == 3 || (entry.flag == 2 && entry.score >= beta) || (entry.flag == 1 && entry.score <= alpha)))
            return entry.score;

        // qsearch
        if (isQsearch)
        {
            bestEval = Evaluate(board);
            if (bestEval >= beta) return bestEval > 0 ? bestEval - ply : bestEval + ply;
            if (bestEval < alpha - 1000) return alpha;
            if (bestEval > alpha) alpha = bestEval;
        }

        // get and order moves
        Move[] moves = board.GetLegalMoves(isQsearch).OrderByDescending(move =>
            move == entry.move ? 100000 : 0 +
            (move.IsCapture ? 10000 + 10 * (int)move.CapturePieceType - (int)move.MovePieceType : 0) +
            history[move.StartSquare.Index, move.TargetSquare.Index]
        ).ToArray();

        // check extentions
        if (inCheck) depth++;

        int origAlpha = alpha;
        for (int i = 0; i < moves.Length; i++)
        {
            Move move = moves[i];

            board.MakeMove(move);
            int eval = 0;
            int reduction = 0;

            // TODO: try to compact this
            if (i == 0)
                eval = -Search(board, depth - 1, -beta, -alpha, ply + 1, timer);
            else
            {
                if (depth >= 3 && i >= 3 && !inCheck)
                    reduction = 1; // TODO: need to look into this
                eval = -Search(board, depth - reduction - 1, -(alpha + 1), -alpha, ply + 1, timer);
                if (eval > alpha && reduction > 0)
                    eval = -Search(board, depth - 1, -(alpha + 1), -alpha, ply + 1, timer);
                if (alpha < eval && eval < beta)
                    eval = -Search(board, depth - 1, -beta, -alpha, ply + 1, timer);
            }

            board.UndoMove(move);

            if (eval > bestEval)
            {
                bestEval = eval;
                bestMove = move;
                if (ply == 0)
                    this.bestMove = move;

                if (eval > alpha)
                    alpha = eval;

                if (alpha >= beta)
                {
                    if (!move.IsCapture)
                    {
                        history[move.StartSquare.Index, move.TargetSquare.Index] += depth * depth;
                    }
                    break;
                }
            }
        }
        if (!isQsearch && moves.Length == 0)
            return inCheck ? ply - INF : 0;

        TTable[key % NUM_ENTRIES] = new TTEntry { key = key, move = bestMove, depth = depth, score = bestEval, flag = bestEval >= beta ? 2 : bestEval > origAlpha ? 3 : 1 };
        return bestEval;
    }

    // simple eval function
    // TODO: retry making it recursive to save tokens
    int Evaluate(Board board)
    {
        int score = 0;

        for (int player = 0; player <= 1; player++)
        {
            for (int pieceType = 1; pieceType <= 6; pieceType++)
            {
                ulong bits = board.GetPieceBitboard((PieceType)pieceType, player == 0);

                while (bits != 0)
                {
                    // material
                    score += PIECE_VALS[pieceType];

                    // centrality (thanks gedas repo)
                    int index = BitboardHelper.ClearAndGetIndexOfLSB(ref bits);
                    int rank = index >> 3;
                    int file = index & 7;
                    int centrality = -Math.Abs(7 - rank - file) - Math.Abs(rank - file);
                    score += centrality * (6 - pieceType); // ignore king
                }
            }
            score = -score;
        }

        return board.IsWhiteToMove ? score : -score;
    }
}