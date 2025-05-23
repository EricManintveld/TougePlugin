local supportAPI_physics = physics.setGentleStop ~= nil -- For disabling physics, not used I think but why not?
local supportAPI_collision = physics.disableCarCollisions ~= nil
local vec = {x=vec3(1,0,0),y=vec3(0,1,0),z=vec3(0,0,1),empty=vec3(),empty2=vec2()}

local teleportTimer = nil

local function dir3FromHeading(heading)
    local h = math.rad(heading + ac.getCompassAngle(vec.z))
    return vec3(-math.sin(h), 0, -math.cos(h))
end

function TeleportExec(pos, rot)
    if supportAPI_collision then physics.disableCarCollisions(0, true) end
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
            print("Sender is nil")
            return
        end
        print("Heading:")
        print(message.heading)
        local direction = dir3FromHeading(message.heading)
        TeleportExec(message.position, direction)
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
