local finishLine = {{-39.9,456.9},{-49.5,454.7}}
local previousPos = nil

local isLookingForFinish = false -- Toggle for this script

local car = ac.getCar(0)

local SessionStates = {
    Off = 0,
    FirstTwo = 1,
    SuddenDeath = 2,
    Finished = 3,
}

-- Events
local sessionStateEvent = ac.OnlineEvent(
    {
        ac.StructItem.key('AS_SessionState'),
        result1 = ac.StructItem.int32(),
        result2 = ac.StructItem.int32(),
        suddenDeathResult = ac.StructItem.int32(),
        sessionState = ac.StructItem.int32()
    }, function (sender, message)  
        if message.sessionState == SessionStates.FirstTwo or message.sessionState == SessionStates.SuddenDeath then
            isLookingForFinish = true
        else
            isLookingForFinish = false
        end
    end)


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

function script.update(dt)
    if car ~= nil and isLookingForFinish then
        local currentPos = car.position
        local currentPos2D = {currentPos.x, currentPos.y}
        if previousPos ~= nil and AreIntersecting({previousPos[1], previousPos[2]}, currentPos2D, finishLine[1], finishLine[2]) then
            print("Crossed line!")
        end
        previousPos = currentPos2D
    end
end