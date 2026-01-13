using System;
using UnityEngine;
using Random = UnityEngine.Random;

namespace MoreMountains.Tools
{
    /// <summary>
    /// Generates a grid using a simple cellular automata simulation.
    /// </summary>
    public class MMGridGeneratorCellularAutomata : MMGridGenerator
    {
        public static int[,] Generate(int width, int height, int seed, int fillPercentage, int birthThreshold,
            int survivalThreshold, int iterations, bool forceBorders)
        {
            int[,] grid = PrepareGrid(ref width, ref height);
            System.Random random = new System.Random(seed);
            Random.InitState(seed);

            int maxX = grid.GetUpperBound(0);
            int maxY = grid.GetUpperBound(1);

            for (int x = 0; x <= maxX; x++)
            {
                for (int y = 0; y <= maxY; y++)
                {
                    bool borderCell = forceBorders && IsBorderCell(x, y, maxX, maxY);
                    grid[x, y] = borderCell ? 1 : (random.Next(0, 100) < fillPercentage ? 1 : 0);
                }
            }

            for (int iteration = 0; iteration < Mathf.Max(1, iterations); iteration++)
            {
                grid = RunSimulationStep(grid, birthThreshold, survivalThreshold, forceBorders);
            }

            return grid;
        }

        private static int[,] RunSimulationStep(int[,] sourceGrid, int birthThreshold, int survivalThreshold, bool forceBorders)
        {
            int maxX = sourceGrid.GetUpperBound(0);
            int maxY = sourceGrid.GetUpperBound(1);
            int[,] newGrid = new int[maxX + 1, maxY + 1];

            for (int x = 0; x <= maxX; x++)
            {
                for (int y = 0; y <= maxY; y++)
                {
                    bool borderCell = forceBorders && IsBorderCell(x, y, maxX, maxY);
                    if (borderCell)
                    {
                        newGrid[x, y] = 1;
                        continue;
                    }

                    int neighborWalls = GetAdjacentWallsCount(sourceGrid, x, y);
                    if (sourceGrid[x, y] == 1)
                    {
                        newGrid[x, y] = neighborWalls >= survivalThreshold ? 1 : 0;
                    }
                    else
                    {
                        newGrid[x, y] = neighborWalls >= birthThreshold ? 1 : 0;
                    }
                }
            }

            return newGrid;
        }

        private static bool IsBorderCell(int x, int y, int maxX, int maxY)
        {
            return (x == 0) || (y == 0) || (x == maxX) || (y == maxY);
        }
    }
}
