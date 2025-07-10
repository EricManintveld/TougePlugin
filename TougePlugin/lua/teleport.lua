local supportAPI_collision = physics.disableCarCollisions ~= nil
local noInputAPI = physics.setCarNoInput ~= nil
local vec = {x=vec3(1,0,0),y=vec3(0,1,0),z=vec3(0,0,1),empty=vec3(),empty2=vec2()}

local teleportTimer = nil

local function dir3FromHeading(heading)
    local h = math.rad(heading + ac.getCompassAngle(vec.z))
    return vec3(-math.sin(h), 0, -math.cos(h))
end

function TeleportExec(pos, rot)
    if supportAPI_collision then physics.disableCarCollisions(0, true) end
    if noInputAPI then physics.setCarNoInput(true) end
    pos.y = FindGroundY(pos)  -- Adjust y-coordinate to ground level
    rot.y = 0 -- Make sure the car is right side up.
    physics.setCarPosition(0, pos, rot)
    
    -- Start teleport timer
    teleportTimer = 10
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
        if noInputAPI then physics.setCarNoInput(message.lockControls) end
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

function script.update(dt)
    if teleportTimer then
        teleportTimer = teleportTimer - dt
        if teleportTimer <= 0 then
            teleportTimer = nil
            if supportAPI_collision then physics.disableCarCollisions(0, false) end
        end
    end
end
