local supportAPI_collision = physics.disableCarCollisions ~= nil
local lockInputAPI = physics.lockUserControlsFor ~= nil
local vec = {x=vec3(1,0,0),y=vec3(0,1,0),z=vec3(0,0,1),empty=vec3(),empty2=vec2()}

local function dir3FromHeading(heading)
    local h = math.rad(heading + ac.getCompassAngle(vec.z))
    return vec3(-math.sin(h), 0, -math.cos(h))
end

function TeleportExec(pos, rot)
    if supportAPI_collision then physics.disableCarCollisions(0, true) end
    if lockInputAPI then physics.lockUserControlsFor(10) end
    pos.y = FindGroundY(pos)  -- Adjust y-coordinate to ground level
    rot.y = 0 -- Make sure the car is right side up.
    physics.setCarPosition(0, pos, rot)
end

local teleportEvent = ac.OnlineEvent(
    {
        ac.StructItem.key('AS_Teleport'),
        position = ac.StructItem.vec3(),
        heading = ac.StructItem.int32(),
    },
    function(sender, message)
        if sender ~= nil then
            return
        end
        local direction = dir3FromHeading(message.heading)
        TeleportExec(message.position, direction)
    end
)

local lockControlsEvent = ac.OnlineEvent(
    {
        ac.StructItem.key('AS_LockControls'),
        lockControls = ac.StructItem.boolean(),
    },
    function (sender, message)
        local lockTimer = 10
        if message.lockControls == false then
            lockTimer = 0
        end

        if lockInputAPI then
            physics.lockUserControlsFor(lockTimer)
        end
    end
)

function FindGroundY(pos)
    local dir = vec3(0, -1, 0)  -- Direction: downward
    local maxDistance = 100.0    -- Maximum distance to check
    local distance = physics.raycastTrack(pos, dir, maxDistance)
    if distance >= 0 then
        return pos.y - distance
    else
        return pos.y  -- Fallback if no hit detected
    end
end
