local finishLine = {{-39.9,456.9},{-49.5,454.7}}
local previousPos = {{0.0, 0.0},{0.0, 0.0}}

local isLookingForFinish = true -- Toggle for this script

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
    local currentPos = car.position
    if AreIntersecting({previousPos.x, previousPos.y}, {currentPos.x, currentPos.y}, finishLine[1], finishLine[2]) then
        print("Crossed line!")
    end
end