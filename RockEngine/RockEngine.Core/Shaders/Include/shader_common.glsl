
// Remove precision qualifiers that cause conflicts
struct Rigidbody {
    vec3 position;
    float padding0;
    vec4 rotation;
    vec3 velocity;
    float padding1;
    vec3 angularVelocity;
    float padding2;
    vec3 force;
    float padding3;
    vec3 torque;
    float padding4;
    float mass;
    float inverseMass;
    float restitution;
    float friction;
    uint bodyType;
    uint colliderIndex;
    uint isActive;
    uint padding5;
    
    // Inertia tensors
    mat4 inertiaTensor;
    mat4 inverseInertiaTensor;
    
    // Center of mass
    vec3 centerOfMass;
    float padding6;
};

struct Collider {
    vec3 position;
    float padding0;
    vec4 rotation;
    vec3 size;          // HALF-EXTENTS (x,y,z half-sizes)
    float padding1;
    float radius;
    float height;
    uint shape;
    uint isTrigger;
    uint rigidbodyIndex;
    uint padding2;
    mat4 worldMatrix;
};

struct CollisionPair {
    uint bodyA;
    uint bodyB;
    uint colliderA;
    uint colliderB;
    vec3 normal;
    float depth;
    vec3 contactPoint;
    float impulse;
    vec3 tangent1;
    float padding1;
    vec3 tangent2;
    float padding2;
    float frictionImpulse1;
    float frictionImpulse2;
};

struct Manifold {
    uint bodyA;
    uint bodyB;
    vec3 normal;
    float depth;
    vec3 contactPoints[4];
    uint contactCount;
    vec3 padding;
};

struct BroadphaseCell {
    uint startIndex;
    uint count;
    uint maxObjects;
    uint padding;
};

struct BroadphaseObject {
    uint bodyId;
    uint cellHash;
    vec3 minBounds;
    float padding0;
    vec3 maxBounds;
    float padding1;
};

struct Constraint {
    uint bodyA;
    uint bodyB;
    vec3 localAnchorA;
    float padding0;
    vec3 localAnchorB;
    float padding1;
    float minDistance;
    float maxDistance;
    float stiffness;
    float damping;
    uint constraintType;
    vec3 padding2;
};

struct Particle {
    vec3 position;
    float lifetime;
    vec3 velocity;
    float size;
    vec4 color;
    uint isActive;
    vec3 padding;
};

struct PhysicsParams {
    vec4 gravity;
    float deltaTime;
    uint particleCount;
    uint bodyCount;
    uint colliderCount;
    uint constraintCount;
    uint maxCollisionPairs; 
    float restitutionScale;
    vec3 padding;
};

struct ContactSolverConstants {
    float deltaTime;
    uint iteration;
    uint totalIterations;
    float baumgarteFactor;
    float baumgarteSlop;
    float maxCorrection;
    float padding1;
    float padding2;
};

struct PositionIntegrationConstants {
    float deltaTime;
    uint bodyCount;
    vec3 padding;
};

// GJK/EPA structures
struct GJKSimplex {
    vec3 points0;
    float weight0;
    vec3 points1;
    float weight1;
    vec3 points2;
    float weight2;
    vec3 points3;
    float weight3;
    int count;
    vec3 direction;
    float closestDistance;
};

struct EPATriangle {
    vec3 normal;
    float distance;
    vec3 points0;
    vec3 points1;
    vec3 points2;
    int isValid;
    vec3 padding;
};

struct SATResult {
    vec3 axis;
    float overlap;
    int isSeparated;
    vec3 contactPoint;
    vec3 edgePointA;
    vec3 edgePointB;
};

struct CCDResult {
    uint bodyA;
    uint bodyB;
    float timeOfImpact;
    vec3 point;
    vec3 normal;
    uint hasCollision;
    vec3 padding;
};

struct ContactPoint {
    vec3 position;
    vec3 normal;
    float depth;
    float warmStartImpulse;
    float frictionImpulse1;
    float frictionImpulse2;
    uint featureType;
    vec3 padding;
};

struct PersistentContact {
    vec3 position;
    vec3 normal;
    float depth;
    float accumulatedImpulse;
    float accumulatedFriction1;
    float accumulatedFriction2;
    uint lifetime;
    uint isValid;
    vec3 padding;
};

// Helper functions
vec4 quatMultiply(vec4 q1, vec4 q2) {
    return vec4(
        q1.w * q2.x + q1.x * q2.w + q1.y * q2.z - q1.z * q2.y,
        q1.w * q2.y - q1.x * q2.z + q1.y * q2.w + q1.z * q2.x,
        q1.w * q2.z + q1.x * q2.y - q1.y * q2.x + q1.z * q2.w,
        q1.w * q2.w - q1.x * q2.x - q1.y * q2.y - q1.z * q2.z
    );
}

vec4 quatFromAxisAngle(vec3 axis, float angle) {
    float halfAngle = angle * 0.5;
    float sinHalf = sin(halfAngle);
    return vec4(axis.x * sinHalf, axis.y * sinHalf, axis.z * sinHalf, cos(halfAngle));
}

vec3 rotateVector(vec3 v, vec4 q) {
    vec3 qv = vec3(q.x, q.y, q.z);
    float s = q.w;
    return 2.0 * dot(qv, v) * qv + (s*s - dot(qv, qv)) * v + 2.0 * s * cross(qv, v);
}

mat3 quatToMat3(vec4 q) {
    float x = q.x, y = q.y, z = q.z, w = q.w;
    float x2 = x + x, y2 = y + y, z2 = z + z;
    float xx = x * x2, xy = x * y2, xz = x * z2;
    float yy = y * y2, yz = y * z2, zz = z * z2;
    float wx = w * x2, wy = w * y2, wz = w * z2;
    
    return mat3(
        1.0 - (yy + zz), xy - wz, xz + wy,
        xy + wz, 1.0 - (xx + zz), yz - wx,
        xz - wy, yz + wx, 1.0 - (xx + yy)
    );
}

vec3 safeNormalize(vec3 v) {
    float len = length(v);
    if (len > 1e-8) {
        return v / len;
    }
    return vec3(0.0, 0.0, 1.0);
}

vec4 safeNormalizeQuat(vec4 q) {
    float len = sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
    if (len > 1e-8) {
        return q / len;
    }
    return vec4(0.0, 0.0, 0.0, 1.0);
}

mat3 mat4ToMat3(mat4 m) {
    return mat3(
        m[0].xyz,
        m[1].xyz,
        m[2].xyz
    );
}

mat3 rotateInertia(mat3 R, mat3 I_local) {
    return R * I_local * transpose(R);
}

vec3 calculateContactVelocity(vec3 position, vec3 velocity, vec3 angularVelocity, vec3 contactPoint) {
    vec3 r = contactPoint - position;
    return velocity + cross(angularVelocity, r);
}

bool isValid(float value) {
    return !isnan(value) && !isinf(value);
}

bool isValidVec3(vec3 v) {
    return !isnan(v.x) && !isnan(v.y) && !isnan(v.z) && 
           !isinf(v.x) && !isinf(v.y) && !isinf(v.z);
}

