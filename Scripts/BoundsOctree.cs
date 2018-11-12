using System.Collections.Generic;
using UnityEngine;

// A Dynamic, Loose Octree for storing any objects that can be described with AABB bounds
// See also: PointOctree, where objects are stored as single points and some code can be simplified
// Octree:	An octree is a tree data structure which divides 3D space into smaller partitions (nodes)
//			and places objects into the appropriate nodes. This allows fast access to objects
//			in an area of interest without having to check every object.
// Dynamic: The octree grows or shrinks as required when objects as added or removed
//			It also splits and merges nodes as appropriate. There is no maximum depth.
//			Nodes have a constant - numObjectsAllowed - which sets the amount of items allowed in a node before it splits.
// Loose:	The octree's nodes can be larger than 1/2 their parent's length and width, so they overlap to some extent.
//			This can alleviate the problem of even tiny objects ending up in large nodes if they're near boundaries.
//			A looseness value of 1.0 will make it a "normal" octree.
// T:		The content of the octree can be anything, since the bounds data is supplied separately.

// Originally written for my game Scraps (http://www.scrapsgame.com) but intended to be general-purpose.
// Copyright 2014 Nition, BSD licence (see LICENCE file). http://nition.co
// Unity-based, but could be adapted to work in pure C#

// Note: For loops are often used here since in some cases (e.g. the IsColliding method)
// they actually give much better performance than using Foreach, even in the compiled build.
// Using a LINQ expression is worse again than Foreach.

//	* 矛盾: 
//	* Octree大概原理也知道
//	* 类中的小变量也非常多，有的小变量，一看也知道是干什么的。有的小变量一看，不知道是干什么的。
//		* 解决方法：
//		* 全部看完，能混个脸熟就混个脸熟，能看懂的小的操作就看懂小的基础操作
public class BoundsOctree<T> {

	// The total amount of objects currently in the tree
	// 总节点个数
	public int Count { get; private set; }

	// Root node of the octree
	// 根节点
	BoundsOctreeNode<T> rootNode;

	// Should be a value between 1 and 2. A multiplier for the base size of a node.
	// 1.0 is a "normal" octree, while values > 1 have overlap
	// 实际节点的大小，要在basseLength的基础上扩大多少
	readonly float looseness;

	// Size that the octree was on creation
	// 初始大小
	// Q: 如何起作用的？
	readonly float initialSize;

	// Minimum side length that a node can be - essentially an alternative to having a max depth
	// Q: ?
	readonly float minSize;

	// For collision visualisation. Automatically removed in builds.
	#if UNITY_EDITOR
	// Q: 为了碰撞显示？
	const int numCollisionsToSave = 4;
	readonly Queue<Bounds> lastBoundsCollisionChecks = new Queue<Bounds>();
	readonly Queue<Ray> lastRayCollisionChecks = new Queue<Ray>();
	#endif

	/// <summary>
	/// Constructor for the bounds octree.
	/// </summary>
	/// <param name="initialWorldSize">Size of the sides of the initial node, in metres. The octree will never shrink smaller than this.</param>
	/// <param name="initialWorldPos">Position of the centre of the initial node.</param>
	/// <param name="minNodeSize">Nodes will stop splitting if the new nodes would be smaller than this (metres).</param>
	/// <param name="loosenessVal">Clamped between 1 and 2. Values > 1 let nodes overlap.</param>
	// 构造函数
	// 基本上是调用BoundsOctreeNode构造函数
	// initialWorldSize: 根节点不考虑looseness的大小
	// initialWorldPos: 根节点的中心位置
	// minNodeSize: 八叉树允许的最小节点大小的1/2，再小将不会创建新的节点
	// loosenessVal: looseness值
	public BoundsOctree(float initialWorldSize, Vector3 initialWorldPos, float minNodeSize, float loosenessVal) {
		if (minNodeSize > initialWorldSize) {
			Debug.LogWarning("Minimum node size must be at least as big as the initial world size. Was: " + minNodeSize + " Adjusted to: " + initialWorldSize);
			minNodeSize = initialWorldSize;
		}
		Count = 0;
		initialSize = initialWorldSize;
		minSize = minNodeSize;
		looseness = Mathf.Clamp(loosenessVal, 1.0f, 2.0f);
		rootNode = new BoundsOctreeNode<T>(initialSize, minSize, loosenessVal, initialWorldPos);
	}

	// #### PUBLIC METHODS ####

	/// <summary>
	/// Add an object.
	/// </summary>
	/// <param name="obj">Object to add.</param>
	/// <param name="objBounds">3D bounding box around the object.</param>
	// 添加物体
	// 如果加不上，扩展八叉树范围
	public void Add(T obj, Bounds objBounds) {
		// Add object or expand the octree until it can be added
		int count = 0; // Safety check against infinite/excessive growth
		// 查实根节点中，加入物体
		while (!rootNode.Add(obj, objBounds)) {
			// 如果没有加上
			// 尝试扩展八叉树大小

			// 扩展八叉树的范围，原来的范围*2
			// 会创建7个新的节点，同旧的根节点，一个8个节点
			// 作为新的根节点的孩子
			Grow(objBounds.center - rootNode.Center);
			if (++count > 20) {
				Debug.LogError("Aborted Add operation as it seemed to be going on forever (" + (count - 1) + ") attempts at growing the octree.");
				return;
			}
		}
		Count++;
	}

	/// <summary>
	/// Remove an object. Makes the assumption that the object only exists once in the tree.
	/// </summary>
	/// <param name="obj">Object to remove.</param>
	/// <returns>True if the object was removed successfully.</returns>
	// 移除物体
	// 尝试ShrinkIfPossible，尝试缩减回原始创建时候的大小
	// (应该是添加物体的时候，可能是灰产生扩展操作，这里再尝试缩回去)
	public bool Remove(T obj) {
		bool removed = rootNode.Remove(obj);

		// See if we can shrink the octree down now that we've removed the item
		if (removed) {
			Count--;
			Shrink();
		}

		return removed;
	}

	/// <summary>
	/// Removes the specified object at the given position. Makes the assumption that the object only exists once in the tree.
	/// </summary>
	/// <param name="obj">Object to remove.</param>
	/// <param name="objBounds">3D bounding box around the object.</param>
	/// <returns>True if the object was removed successfully.</returns>
	// 同上Remove
	public bool Remove(T obj, Bounds objBounds) {
		bool removed = rootNode.Remove(obj, objBounds);

		// See if we can shrink the octree down now that we've removed the item
		if (removed) {
			Count--;
			Shrink();
		}

		return removed;
	}

	/// <summary>
	/// Check if the specified bounds intersect with anything in the tree. See also: GetColliding.
	/// </summary>
	/// <param name="checkBounds">bounds to check.</param>
	/// <returns>True if there was a collision.</returns>

	// 直接调用BoundsOctreeNode中对应函数
	public bool IsColliding(Bounds checkBounds) {
		//#if UNITY_EDITOR
		// For debugging
		//AddCollisionCheck(checkBounds);
		//#endif
		return rootNode.IsColliding(ref checkBounds);
	}

	/// <summary>
	/// Check if the specified ray intersects with anything in the tree. See also: GetColliding.
	/// </summary>
	/// <param name="checkRay">ray to check.</param>
	/// <param name="maxDistance">distance to check.</param>
	/// <returns>True if there was a collision.</returns>

	// 直接调用BoundsOctreeNode中对应函数
	public bool IsColliding(Ray checkRay, float maxDistance) {
		//#if UNITY_EDITOR
		// For debugging
		//AddCollisionCheck(checkRay);
		//#endif
		return rootNode.IsColliding(ref checkRay, maxDistance);
	}

	/// <summary>
	/// Returns an array of objects that intersect with the specified bounds, if any. Otherwise returns an empty array. See also: IsColliding.
	/// </summary>
	/// <param name="collidingWith">list to store intersections.</param>
	/// <param name="checkBounds">bounds to check.</param>
	/// <returns>Objects that intersect with the specified bounds.</returns>

	// 直接调用BoundsOctreeNode中函数
	public void GetColliding(List<T> collidingWith, Bounds checkBounds) {
		//#if UNITY_EDITOR
		// For debugging
		//AddCollisionCheck(checkBounds);
		//#endif
		rootNode.GetColliding(ref checkBounds, collidingWith);
	}

	/// <summary>
	/// Returns an array of objects that intersect with the specified ray, if any. Otherwise returns an empty array. See also: IsColliding.
	/// </summary>
	/// <param name="collidingWith">list to store intersections.</param>
	/// <param name="checkRay">ray to check.</param>
	/// <param name="maxDistance">distance to check.</param>
	/// <returns>Objects that intersect with the specified ray.</returns>

	// 直接调用BoundsOctreeNode中函数
	public void GetColliding(List<T> collidingWith, Ray checkRay, float maxDistance = float.PositiveInfinity) {
		//#if UNITY_EDITOR
		// For debugging
		//AddCollisionCheck(checkRay);
		//#endif
		rootNode.GetColliding(ref checkRay, collidingWith, maxDistance);
	}

	// 计算摄像机Camera的6个平面
	// 调用BoundsOctreeNode中函数
	public List<T> GetWithinFrustum(Camera cam) {
		var planes = GeometryUtility.CalculateFrustumPlanes(cam);

		var list = new List<T>();
		rootNode.GetWithinFrustum(planes, list);
		return list;
	}

	// 得到根节点的Bounds
	public Bounds GetMaxBounds() {
		return rootNode.GetBounds();
	}

	/// <summary>
	/// Draws node boundaries visually for debugging.
	/// Must be called from OnDrawGizmos externally. See also: DrawAllObjects.
	/// </summary>
	// 直接调用BoundsOctreeNode函数
	public void DrawAllBounds() {
		rootNode.DrawAllBounds();
	}

	/// <summary>
	/// Draws the bounds of all objects in the tree visually for debugging.
	/// Must be called from OnDrawGizmos externally. See also: DrawAllBounds.
	/// </summary>
	// 直接调用BoundsOctreeNode函数
	public void DrawAllObjects() {
		rootNode.DrawAllObjects();
	}

	// Intended for debugging. Must be called from OnDrawGizmos externally
	// See also DrawAllBounds and DrawAllObjects
	/// <summary>
	/// Visualises collision checks from IsColliding and GetColliding.
	/// Collision visualisation code is automatically removed from builds so that collision checks aren't slowed down.
	/// </summary>
	#if UNITY_EDITOR

	// 调试碰撞
	// 画出所有的lastBoundsCollisionChecks, lastRayCollisionChecks
	public void DrawCollisionChecks() {
		int count = 0;

		// 画出所有的lastBoundsCollisionChecks
		foreach (Bounds collisionCheck in lastBoundsCollisionChecks) {
			Gizmos.color = new Color(1.0f, 1.0f - ((float)count / numCollisionsToSave), 1.0f);
			Gizmos.DrawCube(collisionCheck.center, collisionCheck.size);
			count++;
		}

		// 画出所有的lastRayCollisionChecks
		foreach (Ray collisionCheck in lastRayCollisionChecks) {
			Gizmos.color = new Color(1.0f, 1.0f - ((float)count / numCollisionsToSave), 1.0f);
			Gizmos.DrawRay(collisionCheck.origin, collisionCheck.direction);
			count++;
		}
		Gizmos.color = Color.white;
	}
	#endif

	// #### PRIVATE METHODS ####

	/// <summary>
	/// Used for visualising collision checks with DrawCollisionChecks.
	/// Automatically removed from builds so that collision checks aren't slowed down.
	/// </summary>
	/// <param name="checkBounds">bounds that were passed in to check for collisions.</param>
	#if UNITY_EDITOR
	// 用于调试，可视化显示碰撞
	void AddCollisionCheck(Bounds checkBounds) {
		lastBoundsCollisionChecks.Enqueue(checkBounds);
		if (lastBoundsCollisionChecks.Count > numCollisionsToSave) {
			lastBoundsCollisionChecks.Dequeue();
		}
	}
	#endif

	/// <summary>
	/// Used for visualising collision checks with DrawCollisionChecks.
	/// Automatically removed from builds so that collision checks aren't slowed down.
	/// </summary>
	/// <param name="checkRay">ray that was passed in to check for collisions.</param>
	#if UNITY_EDITOR
	// 用于调试，可视化显示碰撞
	void AddCollisionCheck(Ray checkRay) {
		lastRayCollisionChecks.Enqueue(checkRay);
		if (lastRayCollisionChecks.Count > numCollisionsToSave) {
			lastRayCollisionChecks.Dequeue();
		}
	}
	#endif

	/// <summary>
	/// Grow the octree to fit in all objects.
	/// </summary>
	/// <param name="direction">Direction to grow.</param>
	// 扩展八叉树的范围，原来的范围*2
	// 会创建7个新的节点，同旧的根节点，一个8个节点
	// 作为新的根节点的孩子
	void Grow(Vector3 direction) {
		int xDirection = direction.x >= 0 ? 1 : -1;
		int yDirection = direction.y >= 0 ? 1 : -1;
		int zDirection = direction.z >= 0 ? 1 : -1;
		BoundsOctreeNode<T> oldRoot = rootNode;
		float half = rootNode.BaseLength / 2;

		// 新的根节点大小，是原来的2倍
		float newLength = rootNode.BaseLength * 2;
		// 新的根节点的中心坐标
		Vector3 newCenter = rootNode.Center + new Vector3(xDirection * half, yDirection * half, zDirection * half);

		// Create a new, bigger octree root node
		rootNode = new BoundsOctreeNode<T>(newLength, minSize, looseness, newCenter);

		// 创建其他的7个子节点，连同旧的root，是8个子节点
		// 作为新的根节点的孩子
		if (oldRoot.HasAnyObjects()) {
			// Create 7 new octree children to go with the old root as children of the new root
			int rootPos = rootNode.BestFitChild(oldRoot.Center);
			BoundsOctreeNode<T>[] children = new BoundsOctreeNode<T>[8];
			for (int i = 0; i < 8; i++) {
				if (i == rootPos) {
					children[i] = oldRoot;
				}
				else {
					// i = 0, i%2 = 0
					// i = 1, i%2 = 1
					xDirection = i % 2 == 0 ? -1 : 1;

					// 上下2层
					yDirection = i > 3 ? -1 : 1;
					zDirection = (i < 2 || (i > 3 && i < 6)) ? -1 : 1;
					children[i] = new BoundsOctreeNode<T>(oldRoot.BaseLength, minSize, looseness, newCenter + new Vector3(xDirection * half, yDirection * half, zDirection * half));
				}
			}

			// Attach the new children to the new root node
			rootNode.SetChildren(children);
		}
	}

	/// <summary>
	/// Shrink the octree if possible, else leave it the same.
	/// </summary>

	// 调用BoundsOctreeNode对应函数
	// 尝试收缩到最开始初始化的大小
	void Shrink() {
		rootNode = rootNode.ShrinkIfPossible(initialSize);
	}
}
