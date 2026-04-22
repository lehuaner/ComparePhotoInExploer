namespace ComparePhotoInExploer;

/// <summary>
/// Form1 的分割线拖动逻辑——拖拽网格分割线调整视窗大小
/// 普通拖动：改变整条分割线（分割线两侧所有列/行的视窗大小）
/// Shift+拖动：只改变分割线两侧两张图片的视窗大小（单元格级别）
/// </summary>
public partial class Form1
{
    // 每个单元格的宽度比例（归一化，同一行总和=1）
    private float[] _cellWidthRatios = Array.Empty<float>();
    // 每个单元格的高度比例（归一化，同一列总和=1）
    private float[] _cellHeightRatios = Array.Empty<float>();

    // 分割线拖动状态
    private bool _isSplitterDragging = false;
    private int _splitterIndex = -1; // 正在拖动的分割线索引
    private bool _splitterIsVertical = true; // true=垂直分割线, false=水平分割线
    private int _hoverSplitterIndex = -1; // 悬停的分割线索引
    private bool _hoverSplitterIsVertical = true; // 悬停分割线方向
    private int _hoverSplitterRow = -1; // 悬停的垂直分割线所在行
    private int _hoverSplitterCol = -1; // 悬停的水平分割线所在列
    private int _splitterRow = -1; // 拖动中的垂直分割线所在行（Shift模式下确定是哪两个单元格）
    private int _splitterCol = -1; // 拖动中的水平分割线所在列（Shift模式下确定是哪两个单元格）

    // 分割线检测阈值（像素）
    private const int SplitterHitTestRadius = 5;

    // 单个视窗最小宽度/高度（像素）
    private const int MinCellSize = 80;

    /// <summary>
    /// 初始化单元格比例数组（在布局变化时调用）
    /// </summary>
    private void InitSplitters()
    {
        int total = Math.Max(1, _cols * _rows);
        _cellWidthRatios = new float[total];
        _cellHeightRatios = new float[total];
        ResetSplitters();
    }

    /// <summary>
    /// 重置所有单元格比例为均分
    /// </summary>
    private void ResetSplitters()
    {
        for (int i = 0; i < _cellWidthRatios.Length; i++)
        {
            _cellWidthRatios[i] = 1f / _cols;
            _cellHeightRatios[i] = 1f / _rows;
        }
    }

    /// <summary>
    /// 检测鼠标位置是否在分割线上，返回分割线信息
    /// 包含单元格边界上的分割线段和华容道空隙区域的延伸边框
    /// </summary>
    private (bool isVertical, int splitterIndex, int rowOrCol) HitTestSplitter(PointF pos)
    {
        if (_imageCount <= 1)
            return (false, -1, -1);

        // 检测垂直分割线（在列 c 和列 c+1 之间，splitterIndex = c）
        for (int c = 0; c < _cols - 1; c++)
        {
            for (int r = 0; r < _rows; r++)
            {
                int cellIdx = r * _cols + c;
                float lineX = GetCellLeft(cellIdx) + GetCellWidth(cellIdx);
                float cellTop = GetCellTop(cellIdx);
                float cellBottom = cellTop + GetCellHeight(cellIdx);

                float dist = Math.Abs(pos.X - lineX);
                if (dist <= SplitterHitTestRadius && pos.Y >= cellTop && pos.Y <= cellBottom)
                {
                    return (true, c, r);
                }
            }
        }

        // 检测水平分割线（在行 r 和行 r+1 之间，splitterIndex = r）
        for (int r = 0; r < _rows - 1; r++)
        {
            for (int c = 0; c < _cols; c++)
            {
                int cellIdx = r * _cols + c;
                float lineY = GetCellTop(cellIdx) + GetCellHeight(cellIdx);
                float cellLeft = GetCellLeft(cellIdx);
                float cellRight = cellLeft + GetCellWidth(cellIdx);

                float dist = Math.Abs(pos.Y - lineY);
                if (dist <= SplitterHitTestRadius && pos.X >= cellLeft && pos.X <= cellRight)
                {
                    return (false, r, c);
                }
            }
        }

        // 检测华容道空隙区域的延伸边框
        var gapRegions = GetGapRegions();
        foreach (var gap in gapRegions)
        {
            var r = gap.Rect;

            // 左边：垂直分割线 col，属于行 row（延续行 row 的垂直线）
            if (Math.Abs(pos.X - r.X) <= SplitterHitTestRadius
                && pos.Y >= r.Y && pos.Y <= r.Y + r.Height)
            {
                return (true, gap.Col, gap.Row);
            }

            // 右边：垂直分割线 col，属于行 row+1（延续行 row+1 的垂直线）
            if (Math.Abs(pos.X - (r.X + r.Width)) <= SplitterHitTestRadius
                && pos.Y >= r.Y && pos.Y <= r.Y + r.Height)
            {
                return (true, gap.Col, gap.Row + 1);
            }

            // 上边：水平分割线 row，属于列 col+1（延续列 col+1 的水平线）
            if (Math.Abs(pos.Y - r.Y) <= SplitterHitTestRadius
                && pos.X >= r.X && pos.X <= r.X + r.Width)
            {
                return (false, gap.Row, gap.Col + 1);
            }

            // 下边：水平分割线 row，属于列 col（延续列 col 的水平线）
            if (Math.Abs(pos.Y - (r.Y + r.Height)) <= SplitterHitTestRadius
                && pos.X >= r.X && pos.X <= r.X + r.Width)
            {
                return (false, gap.Row, gap.Col);
            }
        }

        return (false, -1, -1);
    }

    /// <summary>
    /// 获取单元格的左边界 X 坐标
    /// </summary>
    private float GetCellLeft(int cellIndex)
    {
        int row = cellIndex / _cols;
        float totalW = this.ClientSize.Width;
        float x = 0;
        for (int c = 0; c < cellIndex % _cols; c++)
        {
            x += _cellWidthRatios[row * _cols + c] * totalW;
        }
        return x;
    }

    /// <summary>
    /// 获取单元格的上边界 Y 坐标
    /// </summary>
    private float GetCellTop(int cellIndex)
    {
        int col = cellIndex % _cols;
        float totalH = this.ClientSize.Height - TitleBarHeight;
        float y = TitleBarHeight;
        for (int r = 0; r < cellIndex / _cols; r++)
        {
            y += _cellHeightRatios[r * _cols + col] * totalH;
        }
        return y;
    }

    /// <summary>
    /// 获取单元格的宽度
    /// </summary>
    private float GetCellWidth(int cellIndex)
    {
        return _cellWidthRatios[cellIndex] * this.ClientSize.Width;
    }

    /// <summary>
    /// 获取单元格的高度
    /// </summary>
    private float GetCellHeight(int cellIndex)
    {
        return _cellHeightRatios[cellIndex] * (this.ClientSize.Height - TitleBarHeight);
    }

    /// <summary>
    /// 开始拖动分割线
    /// </summary>
    private void StartSplitterDrag(bool isVertical, int splitterIndex, int rowOrCol)
    {
        _isSplitterDragging = true;
        _splitterIsVertical = isVertical;
        _splitterIndex = splitterIndex;
        if (isVertical)
            _splitterRow = rowOrCol;
        else
            _splitterCol = rowOrCol;
    }

    /// <summary>
    /// 实时检测 Shift 键是否按下
    /// </summary>
    private static bool IsShiftPressed()
    {
        return (NativeMethods.GetAsyncKeyState(Keys.ShiftKey) & 0x8000) != 0;
    }

    /// <summary>
    /// 拖动分割线时更新比例（实时检测 Shift 状态）
    /// </summary>
    private void UpdateSplitterDrag(float deltaX, float deltaY)
    {
        if (!_isSplitterDragging || _splitterIndex < 0)
            return;

        bool shiftMode = IsShiftPressed();

        if (_splitterIsVertical)
            ApplyVerticalSplitterDelta(deltaX, shiftMode);
        else
            ApplyHorizontalSplitterDelta(deltaY, shiftMode);
    }

    /// <summary>
    /// 垂直分割线拖动：调整单元格宽度比例
    /// 普通模式：所有行的该分割线一起动（整列变化）
    /// Shift模式：只改变分割线所在行的两个单元格
    /// </summary>
    private void ApplyVerticalSplitterDelta(float deltaX, bool shiftMode)
    {
        float totalW = this.ClientSize.Width;
        if (totalW <= 0) return;
        float deltaRatio = deltaX / totalW;
        float minRatio = (float)MinCellSize / totalW;

        if (shiftMode)
        {
            // Shift 模式：只改变分割线所在行的两个单元格
            int leftCell = _splitterRow * _cols + _splitterIndex;
            int rightCell = _splitterRow * _cols + _splitterIndex + 1;

            float newLeft = _cellWidthRatios[leftCell] + deltaRatio;
            float newRight = _cellWidthRatios[rightCell] - deltaRatio;

            if (newLeft < minRatio || newRight < minRatio)
                return;

            // 试算新比例，检查是否会导致对角重叠（内缩）
            var testRatios = (float[])_cellWidthRatios.Clone();
            testRatios[leftCell] = newLeft;
            testRatios[rightCell] = newRight;
            if (HasDiagonalOverlaps(testRatios, _cellHeightRatios))
                return;

            _cellWidthRatios[leftCell] = newLeft;
            _cellWidthRatios[rightCell] = newRight;
        }
        else
        {
            // 普通模式：所有行的该分割线一起动（整列变化）
            for (int r = 0; r < _rows; r++)
            {
                float leftTotal = 0f;
                for (int c = 0; c <= _splitterIndex; c++)
                    leftTotal += _cellWidthRatios[r * _cols + c];

                if (leftTotal <= 0) continue;

                float rightTotal = 0f;
                for (int c = _splitterIndex + 1; c < _cols; c++)
                    rightTotal += _cellWidthRatios[r * _cols + c];

                if (rightTotal <= 0) continue;

                float newLeftTotal = leftTotal + deltaRatio;
                float newRightTotal = rightTotal - deltaRatio;

                if (newLeftTotal < minRatio || newRightTotal < minRatio)
                    continue;

                float leftScale = newLeftTotal / leftTotal;
                for (int c = 0; c <= _splitterIndex; c++)
                    _cellWidthRatios[r * _cols + c] *= leftScale;

                float rightScale = newRightTotal / rightTotal;
                for (int c = _splitterIndex + 1; c < _cols; c++)
                    _cellWidthRatios[r * _cols + c] *= rightScale;
            }
        }
    }

    /// <summary>
    /// 水平分割线拖动：调整单元格高度比例
    /// 普通模式：所有列的该分割线一起动（整行变化）
    /// Shift模式：只改变分割线所在列的两个单元格
    /// </summary>
    private void ApplyHorizontalSplitterDelta(float deltaY, bool shiftMode)
    {
        float totalH = this.ClientSize.Height - TitleBarHeight;
        if (totalH <= 0) return;
        float deltaRatio = deltaY / totalH;
        float minRatio = (float)MinCellSize / totalH;

        if (shiftMode)
        {
            // Shift 模式：只改变分割线所在列的两个单元格
            int topCell = _splitterIndex * _cols + _splitterCol;
            int bottomCell = (_splitterIndex + 1) * _cols + _splitterCol;

            float newTop = _cellHeightRatios[topCell] + deltaRatio;
            float newBottom = _cellHeightRatios[bottomCell] - deltaRatio;

            if (newTop < minRatio || newBottom < minRatio)
                return;

            // 试算新比例，检查是否会导致对角重叠（内缩）
            var testRatios = (float[])_cellHeightRatios.Clone();
            testRatios[topCell] = newTop;
            testRatios[bottomCell] = newBottom;
            if (HasDiagonalOverlaps(_cellWidthRatios, testRatios))
                return;

            _cellHeightRatios[topCell] = newTop;
            _cellHeightRatios[bottomCell] = newBottom;
        }
        else
        {
            // 普通模式：所有列的该分割线一起动（整行变化）
            for (int c = 0; c < _cols; c++)
            {
                float topTotal = 0f;
                for (int r = 0; r <= _splitterIndex; r++)
                    topTotal += _cellHeightRatios[r * _cols + c];

                if (topTotal <= 0) continue;

                float bottomTotal = 0f;
                for (int r = _splitterIndex + 1; r < _rows; r++)
                    bottomTotal += _cellHeightRatios[r * _cols + c];

                if (bottomTotal <= 0) continue;

                float newTopTotal = topTotal + deltaRatio;
                float newBottomTotal = bottomTotal - deltaRatio;

                if (newTopTotal < minRatio || newBottomTotal < minRatio)
                    continue;

                float topScale = newTopTotal / topTotal;
                for (int r = 0; r <= _splitterIndex; r++)
                    _cellHeightRatios[r * _cols + c] *= topScale;

                float bottomScale = newBottomTotal / bottomTotal;
                for (int r = _splitterIndex + 1; r < _rows; r++)
                    _cellHeightRatios[r * _cols + c] *= bottomScale;
            }
        }
    }

    /// <summary>
    /// 约束所有列比例，确保每列不小于 MinCellSize
    /// </summary>
    private void ClampAllColSplits()
    {
        if (_cellWidthRatios.Length == 0) return;
        float totalW = this.ClientSize.Width;
        if (totalW <= 0) return;
        float minRatio = (float)MinCellSize / totalW;

        for (int i = 0; i < _cellWidthRatios.Length; i++)
        {
            if (_cellWidthRatios[i] < minRatio)
                _cellWidthRatios[i] = minRatio;
        }

        // 按行归一化使每行宽度总和=1
        for (int r = 0; r < _rows; r++)
        {
            float sum = 0f;
            for (int c = 0; c < _cols; c++)
                sum += _cellWidthRatios[r * _cols + c];
            if (sum > 0)
            {
                for (int c = 0; c < _cols; c++)
                    _cellWidthRatios[r * _cols + c] /= sum;
            }
        }
    }

    /// <summary>
    /// 约束所有行比例，确保每行不小于 MinCellSize
    /// </summary>
    private void ClampAllRowSplits()
    {
        if (_cellHeightRatios.Length == 0) return;
        float totalH = this.ClientSize.Height - TitleBarHeight;
        if (totalH <= 0) return;
        float minRatio = (float)MinCellSize / totalH;

        for (int i = 0; i < _cellHeightRatios.Length; i++)
        {
            if (_cellHeightRatios[i] < minRatio)
                _cellHeightRatios[i] = minRatio;
        }

        // 按列归一化使每列高度总和=1
        for (int c = 0; c < _cols; c++)
        {
            float sum = 0f;
            for (int r = 0; r < _rows; r++)
                sum += _cellHeightRatios[r * _cols + c];
            if (sum > 0)
            {
                for (int r = 0; r < _rows; r++)
                    _cellHeightRatios[r * _cols + c] /= sum;
            }
        }
    }

    /// <summary>
    /// 检测给定宽度和高度比例数组是否会导致单元格对角重叠（内缩问题）
    /// 两种重叠情况：
    /// 1. 主对角线：格子(r,c)的右下角侵入格子(r+1,c+1)的左上角
    ///    条件：第r行比第r+1行右边界更靠右 且 第c列比第c+1列底边界更靠下
    /// 2. 副对角线：格子(r,c+1)的左下角侵入格子(r+1,c)的右上角
    ///    条件：第r行比第r+1行左边界更靠左 且 第c+1列比第c列底边界更靠下
    /// </summary>
    private bool HasDiagonalOverlaps(float[] widthRatios, float[] heightRatios)
    {
        if (_cols <= 1 || _rows <= 1)
            return false;

        // 预计算每行各列的右边界（累积宽度）
        var rowRightEdges = new float[_rows, _cols];
        for (int r = 0; r < _rows; r++)
        {
            float x = 0;
            for (int c = 0; c < _cols; c++)
            {
                x += widthRatios[r * _cols + c];
                rowRightEdges[r, c] = x;
            }
        }

        // 预计算每列各行的底边界（累积高度）
        var colBottomEdges = new float[_rows, _cols];
        for (int c = 0; c < _cols; c++)
        {
            float y = 0;
            for (int r = 0; r < _rows; r++)
            {
                y += heightRatios[r * _cols + c];
                colBottomEdges[r, c] = y;
            }
        }

        const float eps = 0.001f;

        for (int r = 0; r < _rows - 1; r++)
        {
            for (int c = 0; c < _cols - 1; c++)
            {
                // 主对角线重叠：格子(r,c) 与 格子(r+1,c+1)
                // 格子(r,c)右边界 > 格子(r+1,c)右边界（第r行比第r+1行更靠右）
                // 格子(r,c)底边界 > 格子(r,c+1)底边界（第c列比第c+1列更靠下）
                if (rowRightEdges[r, c] > rowRightEdges[r + 1, c] + eps
                    && colBottomEdges[r, c] > colBottomEdges[r, c + 1] + eps)
                    return true;

                // 副对角线重叠：格子(r,c+1) 与 格子(r+1,c)
                // 第r行右边界 < 第r+1行右边界（第r行更靠左，即格子(r,c+1)向左侵入格子(r+1,c)区域）
                // 第c+1列底边界 > 第c列底边界（第c+1列更靠下，即格子(r,c+1)向下侵入格子(r+1,c)区域）
                if (rowRightEdges[r, c] < rowRightEdges[r + 1, c] - eps
                    && colBottomEdges[r, c + 1] > colBottomEdges[r, c] + eps)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 结束分割线拖动
    /// </summary>
    private void EndSplitterDrag()
    {
        _isSplitterDragging = false;
        _splitterIndex = -1;
        _splitterRow = -1;
        _splitterCol = -1;
    }

    /// <summary>
    /// 获取悬停分割线的光标类型
    /// </summary>
    private Cursor GetSplitterCursor(bool isVertical) => isVertical ? Cursors.SizeWE : Cursors.SizeNS;

    /// <summary>
    /// 华容道空隙区域信息（含行列索引，用于边框高亮匹配）
    /// </summary>
    private struct GapRegion
    {
        public RectangleF Rect;
        public int Row; // 空隙所在的上行索引（r）
        public int Col; // 空隙所在的左列索引（c）
    }

    /// <summary>
    /// 计算所有单元格区域之间的"华容道"空隙区域（Shift拖动后可能出现的未覆盖区域）
    /// 每个空隙对应交叉点(r,c)，其四条边分别对应：
    /// - 左边：垂直分割线 c，行 r 的延伸
    /// - 右边：垂直分割线 c，行 r+1 的延伸
    /// - 上边：水平分割线 r，列 c 的延伸
    /// - 下边：水平分割线 r，列 c+1 的延伸
    /// </summary>
    private List<GapRegion> GetGapRegions()
    {
        var gaps = new List<GapRegion>();
        if (_cols <= 1 || _rows <= 1)
            return gaps;

        // 收集每行各列的右边界 X 坐标
        var rowRightEdges = new float[_rows, _cols];
        for (int r = 0; r < _rows; r++)
        {
            float x = 0;
            for (int c = 0; c < _cols; c++)
            {
                float w = _cellWidthRatios[r * _cols + c] * this.ClientSize.Width;
                x += w;
                rowRightEdges[r, c] = x;
            }
        }

        // 收集每列各行的底边界 Y 坐标
        var colBottomEdges = new float[_rows, _cols];
        for (int c = 0; c < _cols; c++)
        {
            float y = TitleBarHeight;
            for (int r = 0; r < _rows; r++)
            {
                float h = _cellHeightRatios[r * _cols + c] * (this.ClientSize.Height - TitleBarHeight);
                y += h;
                colBottomEdges[r, c] = y;
            }
        }

        for (int r = 0; r < _rows - 1; r++)
        {
            for (int c = 0; c < _cols - 1; c++)
            {
                float rightEdge = rowRightEdges[r, c];
                float bottomEdge = colBottomEdges[r, c];

                float diagLeft = rowRightEdges[r + 1, c];
                float diagTop = colBottomEdges[r, c + 1];

                float gapX = Math.Min(rightEdge, diagLeft);
                float gapY = Math.Min(bottomEdge, diagTop);
                float gapRight = Math.Max(rightEdge, diagLeft);
                float gapBottom = Math.Max(bottomEdge, diagTop);

                float gapW = gapRight - gapX;
                float gapH = gapBottom - gapY;

                if (gapW > 0.5f && gapH > 0.5f)
                {
                    gaps.Add(new GapRegion
                    {
                        Rect = new RectangleF(gapX, gapY, gapW, gapH),
                        Row = r,
                        Col = c
                    });
                }
            }
        }

        return gaps;
    }
}
