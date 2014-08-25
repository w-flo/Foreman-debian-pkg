﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace Foreman
{
	public enum Direction { Up, Down, Left, Right }
	public struct TooltipInfo
	{
		public TooltipInfo(Point screenLocation, Point screenSize, Direction direction, String text)
		{
			ScreenLocation = screenLocation;
			ScreenSize = screenSize;
			Direction = direction;
			Text = text;
		}

		public Point ScreenLocation;
		public Point ScreenSize;
		public Direction Direction;
		public String Text;
	}

	public partial class ProductionGraphViewer : UserControl
	{
		public HashSet<GraphElement> Elements = new HashSet<GraphElement>();
		public ProductionGraph Graph = new ProductionGraph();
		private List<Item> Demands = new List<Item>();
		public bool IsBeingDragged { get; private set; }
		private Point lastMouseDragPoint;
		public Point ViewOffset;
		public float ViewScale = 1f;
		public NodeElement SelectedNode = null;
		public NodeElement MousedNode = null;
		public NodeElement ClickedNode = null;
		private GraphElement draggedElement;
		public GraphElement DraggedElement
		{
			get { return draggedElement; }
			set { dragStartScreenPoint = Control.MousePosition; draggedElement = value; }
		}
		private Point dragStartScreenPoint;
		public Queue<TooltipInfo> toolTipsToDraw = new Queue<TooltipInfo>();

		public NodeElement LinkDragStartNode = null;
		public Item LinkDragItem = null;
		public LinkType LinkDragStartLinkType;
		public NodeElement LinkDragEndNode = null;

		private Rectangle graphBounds
		{
			get
			{
				int width = 0;
				int height = 0;
				foreach (NodeElement element in Elements.OfType<NodeElement>())
				{
					height = Math.Max(element.Y + element.Height, height);
					width = Math.Max(element.X + element.Width, width);
				}
				return new Rectangle(0, 0, width + 20, height + 20);
			}
		}
		
		public ProductionGraphViewer()
		{
			InitializeComponent();
			MouseWheel += new MouseEventHandler(ProductionGraphViewer_MouseWheel);
		}

		public void AddDemands(IEnumerable<Item> list)
		{
			foreach (Item item in list)
			{
				ConsumerNode node = ConsumerNode.Create(item, Graph);
			}

			AddRemoveElements();
			PositionNodes();
		}

		public void AddDemand(Item item)
		{
			AddDemands(new List<Item> { item });
		}

		public void UpdateNodes()
		{
			foreach (NodeElement node in Elements.OfType<NodeElement>())
			{
				node.Update();
			}
			Invalidate();
		}

		public void AddRemoveElements()
		{
			Elements.RemoveWhere(e => e is LinkElement && !Graph.GetAllNodeLinks().Contains((e as LinkElement).DisplayedLink));
			Elements.RemoveWhere(e => e is NodeElement && !Graph.Nodes.Contains((e as NodeElement).DisplayedNode));

			foreach (ProductionNode node in Graph.Nodes)
			{
				if (!Elements.OfType<NodeElement>().Any(e => e.DisplayedNode == node))
				{
					Elements.Add(new NodeElement(node, this));
				}
			}

			foreach (NodeLink link in Graph.GetAllNodeLinks())
			{
				if (!Elements.OfType<LinkElement>().Any(e => e.DisplayedLink == link))
				{
					Elements.Add(new LinkElement(this, link));
				}
			}
			UpdateNodes();
			Invalidate();
		}

		public NodeElement GetElementForNode(ProductionNode node)
		{
			return Elements.OfType<NodeElement>().FirstOrDefault(e => e.DisplayedNode == node);
		}

		public void PositionNodes()
		{
			if (!Elements.Any())
			{
				return;
			}
			var nodeOrder = Graph.GetTopologicalSort();
			nodeOrder.Reverse();
			var pathMatrix = Graph.PathMatrix;

			List<ProductionNode>[] nodePositions = new List<ProductionNode>[nodeOrder.Count()];
			for (int i = 0; i < nodePositions.Count(); i++)
			{
				nodePositions[i] = new List<ProductionNode>();
			}

			nodePositions.First().AddRange(nodeOrder.OfType<ConsumerNode>());
			foreach (RecipeNode node in nodeOrder.OfType<RecipeNode>())
			{
				bool PositionFound = false;

				for (int i = nodePositions.Count() - 1; i >= 0 && !PositionFound; i--)
				{
					foreach (ProductionNode listNode in nodePositions[i])
					{
						if (listNode.CanUltimatelyTakeFrom(node))
						{
							nodePositions[i + 1].Add(node);
							PositionFound = true;
							break;
						}
					}
				}

				if (!PositionFound)
				{
					nodePositions.First().Add(node);
				}
			}
			nodePositions.Last().AddRange(nodeOrder.OfType<SupplyNode>());

			int marginX = 100;
			int marginY = 200;
			int y = marginY;
			int[] tierWidths = new int[nodePositions.Count()];
			for (int i = 0; i < nodePositions.Count(); i++)
			{
				var list = nodePositions[i];
				int maxHeight = 0;
				int x = marginX;

				foreach (var node in list)
				{
					NodeElement control = GetElementForNode(node);
					control.X = x;
					control.Y = y;

					x += control.Width + marginX;
					maxHeight = Math.Max(control.Height, maxHeight);
				}

				if (maxHeight > 0) // Don't add any height for empty tiers
				{
					y += maxHeight + marginY;
				}

				tierWidths[i] = x;
			}

			int centrePoint = tierWidths.Last(i => i > marginX) / 2;
			for (int i = tierWidths.Count() - 1; i >= 0; i--)
			{
				int offset = centrePoint - tierWidths[i] / 2;

				foreach (var node in nodePositions[i])
				{
					NodeElement element = GetElementForNode(node);
					element.X = element.X + offset;
				}
			}

			Invalidate(true);
		}

		public IEnumerable<GraphElement> GetPaintingOrder()
		{
			foreach (LinkElement element in Elements.OfType<LinkElement>())
			{
				yield return element;
			}
			foreach (NodeElement element in Elements.OfType<NodeElement>())
			{
				yield return element;
			}
			foreach (DraggedLinkElement element in Elements.OfType<DraggedLinkElement>())
			{
				yield return element;
			}
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			base.OnPaint(e);
			e.Graphics.ResetTransform();
			e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
			e.Graphics.Clear(this.BackColor);
			e.Graphics.TranslateTransform(ViewOffset.X, ViewOffset.Y);
			e.Graphics.ScaleTransform(ViewScale, ViewScale);
			
			foreach (GraphElement element in GetPaintingOrder())
			{
				e.Graphics.TranslateTransform(element.X, element.Y);
				element.Paint(e.Graphics);
				e.Graphics.TranslateTransform(-element.X, -element.Y);
			}
			
			e.Graphics.ResetTransform();
			while (toolTipsToDraw.Any())
			{
				var tt = toolTipsToDraw.Dequeue();

				if (tt.Text != null)
				{
					DrawTooltip(tt.ScreenLocation, tt.Text, tt.Direction, e.Graphics);
				}
				else
				{
					DrawTooltip(tt.ScreenLocation, tt.ScreenSize, tt.Direction, e.Graphics);
				}
			}
		}

		private void DrawTooltip(Point point, String text, Direction direction, Graphics graphics)
		{
			Font font = new Font(FontFamily.GenericSansSerif, 10);
			SizeF stringSize = graphics.MeasureString(text, font);
			DrawTooltip(point, new Point((int)stringSize.Width, (int)stringSize.Height), direction, graphics, text);
		}
		
		private void DrawTooltip(Point screenArrowPoint, Point screenSize, Direction direction, Graphics graphics, String text = "")
		{
			Font font = new Font(FontFamily.GenericSansSerif, 10);
			int border = 2;
			int arrowSize = 10;
			Point arrowPoint1 = new Point();
			Point arrowPoint2 = new Point();

			switch (direction){
				case Direction.Down:
					arrowPoint1 = new Point(screenArrowPoint.X - arrowSize / 2, screenArrowPoint.Y - arrowSize);
					arrowPoint2 = new Point(screenArrowPoint.X + arrowSize / 2, screenArrowPoint.Y - arrowSize);
					break;
				case Direction.Left:
					arrowPoint1 = new Point(screenArrowPoint.X - arrowSize, screenArrowPoint.Y - arrowSize / 2);
					arrowPoint1 = new Point(screenArrowPoint.X - arrowSize, screenArrowPoint.Y + arrowSize / 2);
					break;
				case Direction.Up:
					arrowPoint1 = new Point(screenArrowPoint.X - arrowSize / 2, screenArrowPoint.Y + arrowSize);
					arrowPoint2 = new Point(screenArrowPoint.X + arrowSize / 2, screenArrowPoint.Y + arrowSize);
					break;
				case Direction.Right:
					arrowPoint1 = new Point(screenArrowPoint.X + arrowSize, screenArrowPoint.Y - arrowSize / 2);
					arrowPoint1 = new Point(screenArrowPoint.X + arrowSize, screenArrowPoint.Y + arrowSize / 2);
					break;
			}

			Rectangle rect = getTooltipScreenBounds(screenArrowPoint, screenSize, direction);

			Point[] points = new Point[]{screenArrowPoint, arrowPoint1, arrowPoint2};
			graphics.FillPolygon(Brushes.DarkGray, points); 
			GraphicsStuff.FillRoundRect(rect.X - border, rect.Y - border, rect.Width + border * 2, rect.Height + border * 2, 3, graphics, Brushes.DarkGray);

			StringFormat centreFormat = new StringFormat();
			centreFormat.Alignment = centreFormat.LineAlignment = StringAlignment.Center;
			graphics.DrawString(text, font, Brushes.White, new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2), centreFormat);
		}

		public Rectangle getTooltipScreenBounds(Point screenArrowPoint, Point screenSize, Direction direction)
		{
			Point centreOffset = new Point();
			int arrowSize = 10;

			switch (direction)
			{
				case Direction.Down:
					centreOffset = new Point(0, -arrowSize - screenSize.Y / 2);
					break;
				case Direction.Left:
					centreOffset = new Point(arrowSize + screenSize.X / 2, 0);
					break;
				case Direction.Up:
					centreOffset = new Point(0, arrowSize + screenSize.Y / 2);
					break;
				case Direction.Right:
					centreOffset = new Point(-arrowSize - screenSize.X / 2, 0);
					break;
			}
			int X = (screenArrowPoint.X + centreOffset.X - screenSize.X / 2);
			int Y = (screenArrowPoint.Y + centreOffset.Y - screenSize.Y / 2);
			int Width = screenSize.X;
			int Height = screenSize.Y;

			return new Rectangle(X, Y, Width, Height);
		}

		public IEnumerable<GraphElement> GetElementsAtPoint(Point point)
		{
			foreach (GraphElement element in GetPaintingOrder().Reverse<GraphElement>())
			{
				if (element.ContainsPoint(Point.Add(point, new Size(-element.X, -element.Y))))
				{
					yield return element;
				}
			}
		}

		private void ProductionGraphViewer_MouseDown(object sender, MouseEventArgs e)
		{
			Focus();

			var element = GetElementsAtPoint(ScreenToGraph(e.Location)).FirstOrDefault();
			if (element != null)
			{
				element.MouseDown(Point.Add(ScreenToGraph(e.Location), new Size(-element.X, -element.Y)), e.Button);
			}

			switch (e.Button)
			{
				case MouseButtons.Middle:
					IsBeingDragged = true;
					lastMouseDragPoint = new Point(e.X, e.Y);
					break;
			}
		}

		private void ProductionGraphViewer_MouseUp(object sender, MouseEventArgs e)
		{
			Focus();

			GraphElement element = GetElementsAtPoint(ScreenToGraph(e.Location)).FirstOrDefault();
			if (element != null)
			{
				element.MouseUp(Point.Add(ScreenToGraph(e.Location), new Size(-element.X, -element.Y)), e.Button);
			}

			DraggedElement = null;

			switch (e.Button)
			{
				case MouseButtons.Middle:
					IsBeingDragged = false;
					break;
			}
		}

		private void ProductionGraphViewer_MouseMove(object sender, MouseEventArgs e)
		{
			var element = GetElementsAtPoint(ScreenToGraph(e.Location)).FirstOrDefault();
			if (element != null)
			{
				element.MouseMoved(Point.Add(ScreenToGraph(e.Location), new Size(-element.X, -element.Y)));
			}

			if (DraggedElement != null)
			{
				Point dragDiff = Point.Add(Control.MousePosition, new Size(-dragStartScreenPoint.X, -dragStartScreenPoint.Y));
				if (dragDiff.X * dragDiff.X + dragDiff.Y * dragDiff.Y > 9) //Only drag if the mouse has moved more than three pixels. This avoids dragging when the user is trying to click.
				{
					DraggedElement.Dragged(Point.Add(ScreenToGraph(e.Location), new Size(-DraggedElement.X, -DraggedElement.Y)));
				}
			}

			if ((Control.MouseButtons & MouseButtons.Middle) != 0)
			{
				ViewOffset = new Point(ViewOffset.X + e.X - lastMouseDragPoint.X, ViewOffset.Y + e.Y - lastMouseDragPoint.Y);
				lastMouseDragPoint = e.Location;
				Invalidate();
			}
			
			Invalidate();
		}

		void ProductionGraphViewer_MouseWheel(object sender, MouseEventArgs e)
		{
			if (e.Delta > 0)
			{
				ViewScale *= 1.1f;
			}
			else
			{
				ViewScale /= 1.1f;
			}

			Invalidate();
		}

		public Point ScreenToGraph(Point point)
		{
			return ScreenToGraph(point.X, point.Y);
		}

		public Point ScreenToGraph(int X, int Y)
		{
			return new Point(Convert.ToInt32((X - ViewOffset.X) / ViewScale), Convert.ToInt32((Y - ViewOffset.Y) / ViewScale));
		}

		public Point GraphToScreen(Point point)
		{
			return GraphToScreen(point.X, point.Y);
		}

		public Point GraphToScreen(int X, int Y)
		{
			return new Point(Convert.ToInt32((X * ViewScale) + ViewOffset.X), Convert.ToInt32((Y * ViewScale) + ViewOffset.Y));
		}

		//Tooltips added with this method will be drawn the next time the graph is repainted.
		public void AddTooltip(TooltipInfo info)
		{
			toolTipsToDraw.Enqueue(info);
		}

		private void ProductionGraphViewer_KeyDown(object sender, KeyEventArgs e)
		{
			if (e.KeyCode == Keys.Delete)
			{
				DeleteNode(SelectedNode);
			}
		}

		public void DeleteNode(NodeElement node)
		{
			if (node != null)
			{
				foreach (NodeLink link in node.DisplayedNode.InputLinks.ToList().Union(node.DisplayedNode.OutputLinks.ToList()))
				{
					Elements.RemoveWhere(le => le is LinkElement && (le as LinkElement).DisplayedLink == link);
				}
				Elements.Remove(node);
				node.DisplayedNode.Destroy();
				UpdateNodes();
				Invalidate();
			}			
		}
	}
}