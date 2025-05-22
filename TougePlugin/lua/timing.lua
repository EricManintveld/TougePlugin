local finishLine = {vec2(0, 0), vec2(0, 0)}
local previousPos = nil

local useTrackFinish = true
local isLookingForFinish = false -- Toggle for this script

local car = ac.getCar(0)

local SessionStates = {
    Off = 0,
    FirstTwo = 1,
    SuddenDeath = 2,
    Finished = 3,
}

-- Events
local finishEvent = ac.OnlineEvent(
    {
        ac.StructItem.key('AS_Finish'),
        lookForFinish = ac.StructItem.boolean(),
    }, function (sender, message)
        if message.lookForFinish and not useTrackFinish then
            isLookingForFinish = true
            print("Looking for finish line!")
            print(finishLine)
        else
            isLookingForFinish = false
            print("NOT looking for finish line!")
        end
    end
)

local initializationEvent = ac.OnlineEvent(
    {
        ac.StructItem.key('AS_Initialization'),
        elo = ac.StructItem.int32(),
        racesCompleted = ac.StructItem.int32(),
        useTrackFinish = ac.StructItem.boolean(),
    }, function (sender, message)
        useTrackFinish = message.useTrackFinish
    end
)

local courseEvent = ac.OnlineEvent(
    {
        ac.StructItem.key('AS_FinishLine'),
        finishPoint1 = ac.StructItem.vec2(),
        finishPoint2 = ac.StructItem.vec2(),
    },
    function(sender, message)
        if sender ~= nil then
            return
        end
        finishLine[1] = message.finishPoint1
        finishLine[2] = message.finishPoint2
        print("Set new finish line!")
    end
)

-- Helper functions
function GetOrientation(p, q, r)
    local val = (q[2] - p[2]) * (r[1] - q[1]) - (q[1] - p[1]) * (r[2] - q[2])
    if val == 0 then
        return 0 -- The lines are colinear.
    end
    if val > 0 then
        return 1
    end
    return 2
end

function AreIntersecting(p1, q1, p2, q2)
    local o1 = GetOrientation(p1, q1, p2)
    local o2 = GetOrientation(p1, q1, q2)
    local o3 = GetOrientation(p2, q2, p1)
    local o4 = GetOrientation(p2, q2, q1)

    if o1 ~= o2 and o3 ~= o4 then
        return true
    end
    return false
end

-- Update
function script.update(dt)
    if car ~= nil and isLookingForFinish then
        local currentPos = car.position
        local currentPos2D = {currentPos.x, currentPos.z}
        if finishLine ~= nil and previousPos ~= nil and AreIntersecting({previousPos[1], previousPos[2]}, currentPos2D, {finishLine[1].x, finishLine[1].y}, {finishLine[2].x, finishLine[2].y}) then
            print("Crossed line!")
            finishEvent({lookForFinish = true})
            currentPos2D = nil -- This way previousPos will be nil
            isLookingForFinish = false -- Stop looking to prevent crossing finish line when teleporting to next race start.
        end
        previousPos = currentPos2D
    end
end