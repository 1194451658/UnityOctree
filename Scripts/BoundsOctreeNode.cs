using System.Collections.Generic;
using UnityEngine;

// A node in a BoundsOctree
// Copyright 2014 Nition, BSD licence (see LICENCE file). http://nition.co

public class BoundsOctreeNode<T> {

	// Centre of this node
	// 节点的中心位置
	public Vector3 Center { get; private set; }

	// Length of this node if it has a looseness of 1.0
	// 不考虑looseness的大小
	// 基础大小
	public float BaseLength { get; private set; }

	// Looseness value for this node
	float looseness;

	// Minimum size for a node in this octree
	// 八叉树允许的最小的1/2节点大小
	// 比此值小的节点，即使节点内存储的物体个数，超过限制，
	// 也不会再创建更小的子节点
	float minSize;

	// Actual length of sides, taking the looseness value into account
	// adjLength = looseness * baseLengthVal;
	// 用来和Center，来创建Bounds的大小
	float adjLength;

	// Bounding box that represents this node
	// 本节点考虑了loose之后的Bounds大小
	// 中心点是Center
	Bounds bounds = default(Bounds);

	// Objects in this node
	// 本节点内，存储的物体
	// （包括：要存储的物体，和物体的Bounds）
	readonly List<OctreeObject> objects = new List<OctreeObject>();

	// Child nodes, if any
	// 子节点
	BoundsOctreeNode<T>[] children = null;

	// 是否有孩子节点
	bool HasChildren { get { return children != null; } }

	// Bounds of potential children to this node. These are actual size (with looseness taken into account), not base size
	// Q: ?
	Bounds[] childBounds;

	// If there are already NUM_OBJECTS_ALLOWED in a node, we split it into children
	// A generally good number seems to be something around 8-15
	// 本节点内，允许存储的物体个数
	const int NUM_OBJECTS_ALLOWED = 8;

	// An object in the octree
	// 存储到本节点的物体的结构
	// （包括：要存储的物体，和物体的Bounds）
	class OctreeObject {
		public T Obj;
		public Bounds Bounds;
	}

	/// <summary>
	/// Constructor.
	/// </summary>
	/// <param name="baseLengthVal">Length of this node, not taking looseness into account.</param>
	/// <param name="minSizeVal">Minimum size of nodes in this octree.</param>
	/// <param name="loosenessVal">Multiplier for baseLengthVal to get the actual size.</param>
	/// <param name="centerVal">Centre position of this node.</param>
	// 构造函数
	// baseLengthVal: 节点的大小
	public BoundsOctreeNode(float baseLengthVal, float minSizeVal, float loosenessVal, Vector3 centerVal) {
		SetValues(baseLengthVal, minSizeVal, loosenessVal, centerVal);
	}

	// #### PUBLIC METHODS ####

	/// <summary>
	/// Add an object.
	/// </summary>
	/// <param name="obj">Object to add.</param>
	/// <param name="objBounds">3D bounding box around the object.</param>
	/// <returns>True if the object fits entirely within this node.</returns>

	// 如果本节点，可以包含此节点，调用SubAdd()函数进行添加
	// SubAdd()，会检测是否需要分裂此节点，生成更小的子节点
	// 并检测最适合的可以完全包围的子节点进行添加，
	// 如果没有最适合的可以包含的子节点，则停留在本层节点
	// 返回：本节点是否添加了此obj
	public bool Add(T obj, Bounds objBounds) {
		// 如果本节点，不包含objBounds
		// 不进行添加
		if (!Encapsulates(bounds, objBounds)) {
			return false;
		}
		SubAdd(obj, objBounds);
		return true;
	}

	/// <summary>
	/// Remove an object. Makes the assumption that the object only exists once in the tree.
	/// </summary>
	/// <param name="obj">Object to remove.</param>
	/// <returns>True if the object was removed successfully.</returns>

	// 从树中，删除物体
	// 复杂度：线性遍历？！
	public bool Remove(T obj) {
		bool removed = false;

		// 从本层中删除
		for (int i = 0; i < objects.Count; i++) {
			if (objects[i].Obj.Equals(obj)) {
				removed = objects.Remove(objects[i]);
				break;
			}
		}

		// 从孩子中取删除
		if (!removed && children != null) {
			for (int i = 0; i < 8; i++) {
				removed = children[i].Remove(obj);
				if (removed) break;
			}
		}

		// 如果有删除
		// 孩子的个数会减少
		if (removed && children != null) {
			// Check if we should merge nodes now that we've removed an item
			// 检查是否需要，合并子节点
			// (所有孩子的个数 <= 分裂值)
			if (ShouldMerge()) {

				// 将8个孩子节点下，存放的物体，添加到本节点中
				// 移除掉孩子节点
				Merge();
			}
		}

		return removed;
	}

	/// <summary>
	/// Removes the specified object at the given position. Makes the assumption that the object only exists once in the tree.
	/// </summary>
	/// <param name="obj">Object to remove.</param>
	/// <param name="objBounds">3D bounding box around the object.</param>
	/// <returns>True if the object was removed successfully.</returns>
	public bool Remove(T obj, Bounds objBounds) {
		if (!Encapsulates(bounds, objBounds)) {
			return false;
		}
		return SubRemove(obj, objBounds);
	}

	/// <summary>
	/// Check if the specified bounds intersect with anything in the tree. See also: GetColliding.
	/// </summary>
	/// <param name="checkBounds">Bounds to check.</param>
	/// <returns>True if there was a collision.</returns>

	//	检查Bounds区域，是否和树中的某物体，相碰撞
	//	Bounds是结构体，所以下面用ref关键字
	//	* 注:
	//		* 单个碰撞的检测，是很简单的
	//		* 只是有一个八叉树分割空间的结构
	//		* 在这个结构上，一层层的比较下去
	public bool IsColliding(ref Bounds checkBounds) {
		// Are the input bounds at least partially in this node?

		// 先检查，是否和整体大小，相碰撞
		if (!bounds.Intersects(checkBounds)) {
			return false;
		}

		// Check against any objects in this node
		// 是否和，本层节点中的物体，相碰撞
		for (int i = 0; i < objects.Count; i++) {
			if (objects[i].Bounds.Intersects(checkBounds)) {
				return true;
			}
		}

		// Check children
		// 是否和，孩子节点中的物体，相碰撞
		if (children != null) {
			for (int i = 0; i < 8; i++) {
				if (children[i].IsColliding(ref checkBounds)) {
					return true;
				}
			}
		}

		return false;
	}

	/// <summary>
	/// Check if the specified ray intersects with anything in the tree. See also: GetColliding.
	/// </summary>
	/// <param name="checkRay">Ray to check.</param>
	/// <param name="maxDistance">Distance to check.</param>
	/// <returns>True if there was a collision.</returns>

	// 检查摄像Ray，是否和树中的某物体，相碰撞
	// 同上面的 IsColliding(Bounds)
	public bool IsColliding(ref Ray checkRay, float maxDistance = float.PositiveInfinity) {
		// Is the input ray at least partially in this node?
		float distance;

		// 先检测，是否和本节点的Bounds相碰撞
		if (!bounds.IntersectRay(checkRay, out distance) || distance > maxDistance) {
			return false;
		}

		// Check against any objects in this node
		// 检查，是否和，本节点中保存的物体，相碰撞
		for (int i = 0; i < objects.Count; i++) {
			if (objects[i].Bounds.IntersectRay(checkRay, out distance) && distance <= maxDistance) {
				return true;
			}
		}

		// Check children
		// 再检查，是否和孩子中的物体，相碰撞
		if (children != null) {
			for (int i = 0; i < 8; i++) {
				if (children[i].IsColliding(ref checkRay, maxDistance)) {
					return true;
				}
			}
		}

		return false;
	}

	/// <summary>
	/// Returns an array of objects that intersect with the specified bounds, if any. Otherwise returns an empty array. See also: IsColliding.
	/// </summary>
	/// <param name="checkBounds">Bounds to check. Passing by ref as it improves performance with structs.</param>
	/// <param name="result">List result.</param>
	/// <returns>Objects that intersect with the specified bounds.</returns>

	// 检查Bounds，碰撞到的物体列表
	// 同IsColliding
	public void GetColliding(ref Bounds checkBounds, List<T> result) {
		// Are the input bounds at least partially in this node?
		if (!bounds.Intersects(checkBounds)) {
			return;
		}

		// Check against any objects in this node
		for (int i = 0; i < objects.Count; i++) {
			if (objects[i].Bounds.Intersects(checkBounds)) {
				result.Add(objects[i].Obj);
			}
		}

		// Check children
		if (children != null) {
			for (int i = 0; i < 8; i++) {
				children[i].GetColliding(ref checkBounds, result);
			}
		}
	}

	/// <summary>
	/// Returns an array of objects that intersect with the specified ray, if any. Otherwise returns an empty array. See also: IsColliding.
	/// </summary>
	/// <param name="checkRay">Ray to check. Passing by ref as it improves performance with structs.</param>
	/// <param name="maxDistance">Distance to check.</param>
	/// <param name="result">List result.</param>
	/// <returns>Objects that intersect with the specified ray.</returns>

	// 获取射线Ray，碰撞到的物体的列表
	// 同IsColliding函数
	public void GetColliding(ref Ray checkRay, List<T> result, float maxDistance = float.PositiveInfinity) {
		float distance;
		// Is the input ray at least partially in this node?
		if (!bounds.IntersectRay(checkRay, out distance) || distance > maxDistance) {
			return;
		}

		// Check against any objects in this node
		for (int i = 0; i < objects.Count; i++) {
			if (objects[i].Bounds.IntersectRay(checkRay, out distance) && distance <= maxDistance) {
				result.Add(objects[i].Obj);
			}
		}

		// Check children
		if (children != null) {
			for (int i = 0; i < 8; i++) {
				children[i].GetColliding(ref checkRay, result, maxDistance);
			}
		}
	}

	// 使用八叉树结构，一层层的调用GeometryUtility.TestPlanesAABB()
	// 检测碰撞
	// 注：GeometryUtility.CalculateFrustumPlanes()函数，可以计算Camera的6个平面
	public void GetWithinFrustum(Plane[] planes, List<T> result) {
		// Is the input node inside the frustum?
		if (!GeometryUtility.TestPlanesAABB(planes, bounds)) {
			return;
		}

		// Check against any objects in this node
		for (int i = 0; i < objects.Count; i++) {
			if (GeometryUtility.TestPlanesAABB(planes, objects[i].Bounds)) {
				result.Add(objects[i].Obj);
			}
		}

		// Check children
		if (children != null) {
			for (int i = 0; i < 8; i++) {
				children[i].GetWithinFrustum(planes, result);
			}
		}
	}

	/// <summary>
	/// Set the 8 children of this octree.
	/// </summary>
	/// <param name="childOctrees">The 8 new child nodes.</param>

	// 手动设置本节点的，8个孩子节点？
	public void SetChildren(BoundsOctreeNode<T>[] childOctrees) {
		if (childOctrees.Length != 8) {
			Debug.LogError("Child octree array must be length 8. Was length: " + childOctrees.Length);
			return;
		}

		children = childOctrees;
	}

	// 返回本节点的Bounds大小
	public Bounds GetBounds() {
		return bounds;
	}

	/// <summary>
	/// Draws node boundaries visually for debugging.
	/// Must be called from OnDrawGizmos externally. See also: DrawAllObjects.
	/// </summary>
	/// <param name="depth">Used for recurcive calls to this method.</param>

	// 画出，所有节点的Bounds
	public void DrawAllBounds(float depth = 0) {
		float tintVal = depth / 7; // Will eventually get values > 1. Color rounds to 1 automatically
		Gizmos.color = new Color(tintVal, 0, 1.0f - tintVal);

		// 画出本节点的Bounds
		Bounds thisBounds = new Bounds(Center, new Vector3(adjLength, adjLength, adjLength));
		Gizmos.DrawWireCube(thisBounds.center, thisBounds.size);

		// 画出孩子节点的Bounds
		if (children != null) {
			depth++;
			for (int i = 0; i < 8; i++) {
				children[i].DrawAllBounds(depth);
			}
		}
		Gizmos.color = Color.white;
	}

	/// <summary>
	/// Draws the bounds of all objects in the tree visually for debugging.
	/// Must be called from OnDrawGizmos externally. See also: DrawAllBounds.
	/// </summary>

	// 使用Gizmos画出所有节点，
	// 内物体的Bounds
	public void DrawAllObjects() {
		float tintVal = BaseLength / 20;
		Gizmos.color = new Color(0, 1.0f - tintVal, tintVal, 0.25f);

		// 画出本节点内的物体
		foreach (OctreeObject obj in objects) {
			Gizmos.DrawCube(obj.Bounds.center, obj.Bounds.size);
		}

		// 画出孩子节点内的物体
		if (children != null) {
			for (int i = 0; i < 8; i++) {
				children[i].DrawAllObjects();
			}
		}

		Gizmos.color = Color.white;
	}

	/// <summary>
	/// We can shrink the octree if:
	/// - This node is >= double minLength in length
	/// - All objects in the root node are within one octant
	/// - This node doesn't have children, or does but 7/8 children are empty
	/// We can also shrink it if there are no objects left at all!
	/// </summary>
	/// <param name="minLength">Minimum dimensions of a node in this octree.</param>
	/// <returns>The new root, or the existing one if we didn't shrink.</returns>

	// 没有调用的地方，需要手动调用
	//	* 主要是：
	//		* 当所有的物体，都集中在一个八分之一空间的时候，
	//		* 进行整个八叉树范围的收缩
	public BoundsOctreeNode<T> ShrinkIfPossible(float minLength) {

		// 如果已经比，要求的minLength*2小
		// 返回
		if (BaseLength < (2 * minLength)) {
			return this;
		}

		// 如果本节点内，没有物体
		// 也没有孩子节点
		// 返回
		if (objects.Count == 0 && (children == null || children.Length == 0)) {
			return this;
		}

		// Check objects in root
		int bestFit = -1;

		// 遍历本节点内，所有物体
		// 主要判断，是否所有物体，都在同一个八分之一空间
		for (int i = 0; i < objects.Count; i++) {
			OctreeObject curObj = objects[i];
			int newBestFit = BestFitChild(curObj.Bounds.center);
			if (i == 0 || newBestFit == bestFit) {
				// In same octant as the other(s). Does it fit completely inside that octant?
				if (Encapsulates(childBounds[newBestFit], curObj.Bounds)) {
					if (bestFit < 0) {
						bestFit = newBestFit;
					}
				}
				else {
					// Nope, so we can't reduce. Otherwise we continue
					return this;
				}
			}
			else {
				return this; // Can't reduce - objects fit in different octants
			}
		}

		// Check objects in children if there are any
		// 检查所有的孩子节点
		// 有物体存放的孩子节点，是否也是上面判断出来的，本节点所有物体所在的八分之一空间
		if (children != null) {
			bool childHadContent = false;
			for (int i = 0; i < children.Length; i++) {
				if (children[i].HasAnyObjects()) {
					if (childHadContent) {
						return this; // Can't shrink - another child had content already
					}
					if (bestFit >= 0 && bestFit != i) {
						return this; // Can't reduce - objects in root are in a different octant to objects in child
					}
					childHadContent = true;
					bestFit = i;
				}
			}
		}

		// Can reduce
		// 如果可以收缩
		// 收缩1/2
		if (children == null) {
			// We don't have any children, so just shrink this node to the new size
			// We already know that everything will still fit in it
			SetValues(BaseLength / 2, minSize, looseness, childBounds[bestFit].center);
			return this;
		}

		// No objects in entire octree
		if (bestFit == -1) {
			return this;
		}

		// We have children. Use the appropriate child as the new root node
		// Q: ?
		return children[bestFit];
	}

	/// <summary>
	/// Find which child node this object would be most likely to fit in.
	/// </summary>
	/// <param name="objBounds">The object's bounds.</param>
	/// <returns>One of the eight child octants.</returns>

	// 根据物体的中心点
	// 查找最合适放置的子节点
	// 返回3个bit位置 000
	// 分别代表z轴，y轴，x轴的方向
	public int BestFitChild(Vector3 objBoundsCenter) {
		return (objBoundsCenter.x <= Center.x ? 0 : 1) + (objBoundsCenter.y >= Center.y ? 0 : 4) + (objBoundsCenter.z <= Center.z ? 0 : 2);
	}

	/*
	/// <summary>
	/// Get the total amount of objects in this node and all its children, grandchildren etc. Useful for debugging.
	/// </summary>
	/// <param name="startingNum">Used by recursive calls to add to the previous total.</param>
	/// <returns>Total objects in this node and its children, grandchildren etc.</returns>
	public int GetTotalObjects(int startingNum = 0) {
		int totalObjects = startingNum + objects.Count;
		if (children != null) {
			for (int i = 0; i < 8; i++) {
				totalObjects += children[i].GetTotalObjects();
			}
		}
		return totalObjects;
	}
	*/

	// #### PRIVATE METHODS ####

	/// <summary>
	/// Set values for this node. 
	/// </summary>
	/// <param name="baseLengthVal">Length of this node, not taking looseness into account.</param>
	/// <param name="minSizeVal">Minimum size of nodes in this octree.</param>
	/// <param name="loosenessVal">Multiplier for baseLengthVal to get the actual size.</param>
	/// <param name="centerVal">Centre position of this node.</param>

	// 初始化本节点的大小，loose值
	// 根据配置，创建本节点的Bounds，和8个子Bounds
	// baseLengthVal: 不考虑loose的原始大小
	// minSizeVal: 八叉树允许的，最小的1/2节点大小（再小将不会创建新的子节点，即使节点内存储的物体个数过多）
	// loosenessVal: loose值
	// centerVal: 本节点Bounds中心点
	void SetValues(float baseLengthVal, float minSizeVal, float loosenessVal, Vector3 centerVal) {
		BaseLength = baseLengthVal;
		minSize = minSizeVal;
		looseness = loosenessVal;
		Center = centerVal;
		adjLength = looseness * baseLengthVal;

		// Create the bounding box.
		Vector3 size = new Vector3(adjLength, adjLength, adjLength);

		// 和Center一起，创建Bounds大小
		bounds = new Bounds(Center, size);

		float quarter = BaseLength / 4f;

		// 计算孩子节点大小（考虑looseness）
		float childActualLength = (BaseLength / 2) * looseness;
		Vector3 childActualSize = new Vector3(childActualLength, childActualLength, childActualLength);

		// 创建8个分区Bounds
		// 分区的中心点，是根据没有loose的大小确定的
		// 分区的大小，是考虑了loose的
		childBounds = new Bounds[8];
		childBounds[0] = new Bounds(Center + new Vector3(-quarter, quarter, -quarter), childActualSize);
		childBounds[1] = new Bounds(Center + new Vector3(quarter, quarter, -quarter), childActualSize);
		childBounds[2] = new Bounds(Center + new Vector3(-quarter, quarter, quarter), childActualSize);
		childBounds[3] = new Bounds(Center + new Vector3(quarter, quarter, quarter), childActualSize);
		childBounds[4] = new Bounds(Center + new Vector3(-quarter, -quarter, -quarter), childActualSize);
		childBounds[5] = new Bounds(Center + new Vector3(quarter, -quarter, -quarter), childActualSize);
		childBounds[6] = new Bounds(Center + new Vector3(-quarter, -quarter, quarter), childActualSize);
		childBounds[7] = new Bounds(Center + new Vector3(quarter, -quarter, quarter), childActualSize);
	}

	/// <summary>
	/// Private counterpart to the public Add method.
	/// </summary>
	/// <param name="obj">Object to add.</param>
	/// <param name="objBounds">3D bounding box around the object.</param>

	// 添加物体到节点
	// 检查是否需要分裂出，更下层的更小的子节点
	// 如果需要此操作，会将本层，可以完全适配到更小子节点的物体，下移
	// 添加此物体，也会查找最合适的子节点
	// 如果没有则会停留在此层节点
	void SubAdd(T obj, Bounds objBounds) {
		// We know it fits at this level if we've got this far

		// We always put things in the deepest possible child
		// So we can skip some checks if there are children aleady
		// 如果，没有子节点
		// 需要检测下是否需要分裂子节点(该过程还会将本节点内保存的物体下移，所以这里有if特殊处理)
		if (!HasChildren) {
			// Just add if few objects are here, or children would be below min size

			// 如果本节点内，存储的物体个数，还未达到允许的最大个数
			if (objects.Count < NUM_OBJECTS_ALLOWED ||
				(BaseLength / 2) < minSize) {		// 此节点的1/2大小，已经小于最小节点(将不会再创建更小的子节点，所以即使已经存储的物体个数过高，也会继续在此节点存储)

				// 存储物体
				// 到本节点
				OctreeObject newObj = new OctreeObject { Obj = obj, Bounds = objBounds };
				objects.Add(newObj);
				return; // We're done. No children yet
			}

			// 需要继续向下
			// 存放到更下层的节点

			// Fits at this level, but we can go deeper. Would it fit there?
			// Create the 8 children
			int bestFitChild;
			if (children == null) {
				// 将按照本节点，分割成更小的8个子节点
				Split();
				if (children == null) {
					Debug.LogError("Child creation failed for an unknown reason. Early exit.");
					return;
				}

				// Now that we have the new children, see if this node's existing objects would fit there
				// 遍历已经加载此节点中的物体
				for (int i = objects.Count - 1; i >= 0; i--) {
					OctreeObject existingObj = objects[i];
					// Find which child the object is closest to based on where the
					// object's center is located in relation to the octree's center

					// 根据物体的中心点
					// 查找最合适放置的子节点
					// 返回3个bit位置 000
					// 分别代表z轴，y轴，x轴的方向
					bestFitChild = BestFitChild(existingObj.Bounds.center);

					// Does it fit?
					// 上面的BestFitChild是根据中心点进行的判断
					// 这里又继续，判断，子节点的Bounds(考虑了looseness)是否包住了，要添加的物体
					// Q: 可能出现，发生在边缘的情况？！导致，子节点，都不能包住物体？！
					// 所以需要设置个合适的looseness ?
					// 没有被子节点完全包围的物体，将仍然保留在本层的节点中
					if (Encapsulates(children[bestFitChild].bounds, existingObj.Bounds)) {
						// 从本节点中移除，
						// 添加到子节点
						children[bestFitChild].SubAdd(existingObj.Obj, existingObj.Bounds); // Go a level deeper					
						objects.Remove(existingObj); // Remove from here
					}
				}	// for (int i = objects.Count - 1; i >= 0; i--) {
			}	// if(children == null)
		} // if(!HasChildren)

		// Handle the new object we're adding now
		// 检查最能包围的子节点
		int bestFit = BestFitChild(objBounds.center);
		if (Encapsulates(children[bestFit].bounds, objBounds)) {
			children[bestFit].SubAdd(obj, objBounds);
		}
		else {
			// Didn't fit in a child. We'll have to it to this node instead
			// 如果没有被包围的子节点，仍然保留在本层的节点中
			OctreeObject newObj = new OctreeObject { Obj = obj, Bounds = objBounds };
			objects.Add(newObj);
		}
	}

	/// <summary>
	/// Private counterpart to the public <see cref="Remove(T, Bounds)"/> method.
	/// </summary>
	/// <param name="obj">Object to remove.</param>
	/// <param name="objBounds">3D bounding box around the object.</param>
	/// <returns>True if the object was removed successfully.</returns>

	// 同上面的，一个，Remove(T obj)函数
	// 从树中，删除物体
	// 复杂度：线性遍历？！
	bool SubRemove(T obj, Bounds objBounds) {
		bool removed = false;

		// 从本层中删除
		for (int i = 0; i < objects.Count; i++) {
			if (objects[i].Obj.Equals(obj)) {
				removed = objects.Remove(objects[i]);
				break;
			}
		}

		// 从孩子中取删除
		// 根据Bounds，可以计算最佳适配孩子节点
		if (!removed && children != null) {
			int bestFitChild = BestFitChild(objBounds.center);
			removed = children[bestFitChild].SubRemove(obj, objBounds);
		}

		// 检查是否需要合并

		// 如果有删除
		// 孩子的个数会减少
		if (removed && children != null) {
			// Check if we should merge nodes now that we've removed an item
			// 检查是否需要，合并子节点
			// (所有孩子的个数 <= 分裂值)
			if (ShouldMerge()) {
				// 将8个孩子节点下，存放的物体，添加到本节点中
				// 移除掉孩子节点
				Merge();
			}
		}

		return removed;
	}

	/// <summary>
	/// Splits the octree into eight children.
	/// </summary>

	// 将按照本节点，分割成更小的8个子节点
	void Split() {
		float quarter = BaseLength / 4f;
		float newLength = BaseLength / 2;

		// 创建8个，子节点
		children = new BoundsOctreeNode<T>[8];
		children[0] = new BoundsOctreeNode<T>(newLength, minSize, looseness, Center + new Vector3(-quarter, quarter, -quarter));
		children[1] = new BoundsOctreeNode<T>(newLength, minSize, looseness, Center + new Vector3(quarter, quarter, -quarter));
		children[2] = new BoundsOctreeNode<T>(newLength, minSize, looseness, Center + new Vector3(-quarter, quarter, quarter));
		children[3] = new BoundsOctreeNode<T>(newLength, minSize, looseness, Center + new Vector3(quarter, quarter, quarter));
		children[4] = new BoundsOctreeNode<T>(newLength, minSize, looseness, Center + new Vector3(-quarter, -quarter, -quarter));
		children[5] = new BoundsOctreeNode<T>(newLength, minSize, looseness, Center + new Vector3(quarter, -quarter, -quarter));
		children[6] = new BoundsOctreeNode<T>(newLength, minSize, looseness, Center + new Vector3(-quarter, -quarter, quarter));
		children[7] = new BoundsOctreeNode<T>(newLength, minSize, looseness, Center + new Vector3(quarter, -quarter, quarter));
	}

	/// <summary>
	/// Merge all children into this node - the opposite of Split.
	/// Note: We only have to check one level down since a merge will never happen if the children already have children,
	/// since THAT won't happen unless there are already too many objects to merge.
	/// </summary>

	// 将8个孩子节点下，存放的物体，添加到本节点中
	// 移除掉孩子节点
	void Merge() {
		// Note: We know children != null or we wouldn't be merging

		// 遍历8个子节点
		for (int i = 0; i < 8; i++) {
			BoundsOctreeNode<T> curChild = children[i];
			// 将孩子下的节点，全都添加到本节点中
			int numObjects = curChild.objects.Count;
			for (int j = numObjects - 1; j >= 0; j--) {
				OctreeObject curObj = curChild.objects[j];
				objects.Add(curObj);
			}
		}
		// Remove the child nodes (and the objects in them - they've been added elsewhere now)
		// 清除孩子节点
		children = null;
	}

	/// <summary>
	/// Checks if outerBounds encapsulates innerBounds.
	/// </summary>
	/// <param name="outerBounds">Outer bounds.</param>
	/// <param name="innerBounds">Inner bounds.</param>
	/// <returns>True if innerBounds is fully encapsulated by outerBounds.</returns>
	// outerBounds是否包含innerBounds
	static bool Encapsulates(Bounds outerBounds, Bounds innerBounds) {
		return outerBounds.Contains(innerBounds.min) &&
			outerBounds.Contains(innerBounds.max);
	}

	/// <summary>
	/// Checks if there are few enough objects in this node and its children that the children should all be merged into this.
	/// </summary>
	/// <returns>True there are less or the same abount of objects in this and its children than numObjectsAllowed.</returns>

	// 检查是否需要，合并子节点
	// (所有孩子的个数 <= 分裂值)
	bool ShouldMerge() {

		// 计算本节点中孩子个数，和
		// 所有子节点中的，孩子个数和
		int totalObjects = objects.Count;
		if (children != null) {
			// 遍历8个孩子节点
			foreach (BoundsOctreeNode<T> child in children) {
				if (child.children != null) {
					// If any of the *children* have children, there are definitely too many to merge,
					// or the child woudl have been merged already
					return false;
				}
				totalObjects += child.objects.Count;
			}
		}

		// 是否，总孩子个数 <= 分裂值
		return totalObjects <= NUM_OBJECTS_ALLOWED;
	}

	/// <summary>
	/// Checks if this node or anything below it has something in it.
	/// </summary>
	/// <returns>True if this node or any of its children, grandchildren etc have something in them</returns>

	// 检查树中，是否有包含物体
	public bool HasAnyObjects() {
		if (objects.Count > 0) return true;

		if (children != null) {
			for (int i = 0; i < 8; i++) {
				if (children[i].HasAnyObjects()) return true;
			}
		}

		return false;
	}
}
