local baseUrl = "http://" .. ac.getServerIP() .. ":" .. ac.getServerPortHTTP() .. "/static/TougePlugin/"

local windowWidth = sim.windowWidth
local windowHeight = sim.windowHeight

-- Scaling
local baseRes = vec2(2560, 1440) -- Reference resolution
local currentRes = vec2(sim.windowWidth, sim.windowHeight)
local scaleFactor = math.min(currentRes.x / baseRes.x, currentRes.y / baseRes.y, 1)

local discreteMode = false

local loadSteamAvatars = false

local scaling = {}

function scaling.vec2(x, y)
  return vec2(x, y) * scaleFactor
end

function scaling.size(size)
  return size * scaleFactor
end

function scaling.get()
  return scaleFactor
end

local elo = -1
local targetElo = -1
local eloAnimSpeed = 5 -- points per second
local hue = 180
local eloNumPos = vec2(66, 26)
local showElo = true
local eloShownAt = nil
local eloHudSize = scaling.vec2(196, 82)

local racesCompleted = 0

local inviteSenderName = ""
local inviteSenderElo = -1
local inviteSenderId = ""
local hasActiveInvite = false
local inviteActivatedAt = nil

local hasInviteMenuOpen = false
local hasCourseSelectOpen = false
local nearbyPlayers = {}
local selectedPlayerId = -1
local lastLobbyStatusRequest = 0
local lobbyCooldown = 1.0  -- Cooldown in seconds
local courseNames = {}
local isOutrunAllowed = false
local isToggleCourse = true

local standings = { 0, 0, 0 }  -- Default, no rounds have been completed.
local standingWindowSize = scaling.vec2(387, 213)
local currentHudState = 0
local HudStates = {
    Off = 0,
    FirstTwo = 1,
    SuddenDeath = 2,
    Finished = 3,
    CatAndMouse = 4,
    NoUpdate = 5,
}
local RaceResults = {
    Tbd = 0,
    Win = 1,
    Loss = 2,
    Tie = 3,
}

local hasTutorialHidden = false
local isTutorialAutoHidden = false
local keyBindings = {
    { key = "N", description = "Invite\nnearby" },
    { key = "I", description = "Invite\nmenu" },
    { key = "F", description = "Forfeit\n"},
    { key = "H", description = "Hide\ntutorial" },
}

local font = ""
local fontBold = ""
local fontSemiBold = ""

local eloHudPath = baseUrl .. "Elo.png"
local standingsHudPath = baseUrl .. "Standings.png"
local playerCardPath = baseUrl .. "PlayerCard.png"
local mKeyPath = baseUrl .. "MKey.png"
local inviteMenuPath = baseUrl .. "InviteMenu.png"
local tutorialPath = baseUrl .. "Tutorial.png"
local keyPath = baseUrl .. "Key.png"
local avatarsPath = baseUrl .. "avatars/"

local togglePath = baseUrl .. "Toggle.png"
local toggleP1 = scaling.vec2(305, 31)
local toggleP2 = scaling.vec2(736, 106)

local notificationMessage = ""
local hasIncomingNotification = false
local notificationActivatedAt = nil

local countdownHudMessage = ""
local isCountdownHudActive = false
local countdownActivatedAt = nil

local forfeitStartTime = nil
local forfeitHoldDuration = 3.0 -- seconds
local forfeitLockout = false

local mouseClickHandled = false

local car = ac.getCar(0)

-- Load fonts
local fontsURL = baseUrl .. "fonts.zip"
web.loadRemoteAssets(fontsURL, function(err, folder)
    if err then
      print("Failed to load fonts: " .. err)
      return
    end

    local fontPath = folder .. "/twoweekendgo-regular.otf"
    local fontPathBold = folder .. "/twoweekendgo-bold.otf"
    local fontPathSemiBold = folder .. "/twoweekendgo-semibold.otf"
    font = string.format("Two Weekend Go:%s", fontPath)
    fontBold = string.format("Two Weekend Go:%s", fontPathBold)
    fontSemiBold = string.format("Two Weekend Go:%s", fontPathSemiBold)
end)

-- Events
local sessionStateEvent = ac.OnlineEvent(
    {
        ac.StructItem.key('AS_SessionState'),
        result1 = ac.StructItem.int32(),
        result2 = ac.StructItem.int32(),
        result3 = ac.StructItem.int32(),
        sessionState = ac.StructItem.int32()
    }, function (sender, message)
        standings[1] = message.result1
        standings[2] = message.result2
        standings[3] = message.result3
        if message.sessionState ~= HudStates.NoUpdate then currentHudState = message.sessionState end
    end)

-- elo helper funciton
function FindEloPos(newElo)
    hue = (newElo / 2000) * 360 - 80
        if newElo >= 1000 then
            eloNumPos = scaling.vec2(66, 26)
        else
            eloNumPos = scaling.vec2(76, 26)
        end
    end

function GetEloColor(eloValue)
    local hueValue = (eloValue / 2000) * 360 - 80
    local r, g, b = HsvToRgb(hueValue, 0.7, 0.8)
    return rgbm(r, g, b, 1)
end

local eloEvent = ac.OnlineEvent(
    {
        ac.StructItem.key('AS_Elo'),
        elo = ac.StructItem.int32()
    }, function (sender, message)
        showElo = true
        targetElo = message.elo
        if discreteMode then
            eloShownAt = os.clock()
        end
    end)

local inviteEvent = ac.OnlineEvent(
    {
        ac.StructItem.key('AS_Invite'),
        inviteSenderName = ac.StructItem.string(),
        inviteRecipientGuid = ac.StructItem.uint64(),
        inviteSenderElo = ac.StructItem.int32(),
        inviteSenderId = ac.StructItem.string(),
        courseName = ac.StructItem.string(),
        isCourse = ac.StructItem.boolean(),
    }, function (sender, message)

        if message.inviteSenderName ~= "" and message.inviteRecipientGuid ~= 1 then
            hasActiveInvite = true
            inviteSenderName = message.inviteSenderName
            inviteSenderElo = message.inviteSenderElo
            inviteSenderId = message.inviteSenderId
            inviteActivatedAt = os.clock()
        end
    end
)

local notificationEvent = ac.OnlineEvent(
    {
        ac.StructItem.key('AS_Notification'),
        message = ac.StructItem.string(64),
        isCountdown = ac.StructItem.boolean(),
    }, function (sender, message)
        if not message.isCountdown then
            notificationMessage = message.message
            hasIncomingNotification = true
            notificationActivatedAt = os.clock()
        else
            countdownHudMessage = message.message
            isCountdownHudActive = true
            countdownActivatedAt = os.clock()
        end
    end
)

local forfeitEvent = ac.OnlineEvent(
    {
        ac.StructItem.key('AS_Forfeit'),
        forfeit = ac.StructItem.boolean()
    }, function (sender, message)
    end
)

local initializationEvent = ac.OnlineEvent(
    {
        ac.StructItem.key('AS_Initialization'),
        elo = ac.StructItem.int32(),
        racesCompleted = ac.StructItem.int32(),
        useTrackFinish = ac.StructItem.boolean(),
        discreteMode = ac.StructItem.boolean(),
        loadSteamAvatars = ac.StructItem.boolean(),
        courseNames = ac.StructItem.string(1024),
        isOutrunAllowed = ac.StructItem.boolean(),
    }, function (sender, message)
        loadSteamAvatars = message.loadSteamAvatars
        isOutrunAllowed = message.isOutrunAllowed
        
        discreteMode = message.discreteMode
        if discreteMode then
            showElo = false
            hasTutorialHidden = true
        end

        elo = message.elo - 100
        targetElo = message.elo
        
        if message.racesCompleted >= 3 then
            isTutorialAutoHidden = true
        end

        for courseName in string.gmatch(message.courseNames, "([^`]+)") do
            table.insert(courseNames, courseName)
        end
    end
)

local lobbyStatusEvent = ac.OnlineEvent({
    ac.StructItem.key('AS_LobbyStatus'),
    nearbyName1 = ac.StructItem.string(),
    nearbyId1 = ac.StructItem.uint64(),
    nearbyInRace1 = ac.StructItem.boolean(),
    nearbyElo1 = ac.StructItem.int32(),
    nearbyName2 = ac.StructItem.string(),
    nearbyId2 = ac.StructItem.uint64(),
    nearbyInRace2 = ac.StructItem.boolean(),
    nearbyElo2 = ac.StructItem.int32(),
    nearbyName3 = ac.StructItem.string(),
    nearbyId3 = ac.StructItem.uint64(),
    nearbyInRace3 = ac.StructItem.boolean(),
    nearbyElo3 = ac.StructItem.int32(),
    nearbyName4 = ac.StructItem.string(),
    nearbyId4 = ac.StructItem.uint64(),
    nearbyInRace4 = ac.StructItem.boolean(),
    nearbyElo4 = ac.StructItem.int32(),
    nearbyName5 = ac.StructItem.string(),
    nearbyId5 = ac.StructItem.uint64(),
    nearbyInRace5 = ac.StructItem.boolean(),
    nearbyElo5 = ac.StructItem.int32(),

}, function (sender, message)
    -- Update nearby players
    nearbyPlayers[1] = {
        name = message.nearbyName1,
        id = message.nearbyId1,
        inRace = message.nearbyInRace1,
        elo = message.nearbyElo1,
      }
    nearbyPlayers[2] = {
        name = message.nearbyName2,
        id = message.nearbyId2,
        inRace = message.nearbyInRace2,
        elo = message.nearbyElo2,
      }
    nearbyPlayers[3] = {
        name = message.nearbyName3,
        id = message.nearbyId3,
        inRace = message.nearbyInRace3,
        elo = message.nearbyElo3,
      }
    nearbyPlayers[4] = {
        name = message.nearbyName4,
        id = message.nearbyId4,
        inRace = message.nearbyInRace4,
        elo = message.nearbyElo4,
      }
    nearbyPlayers[5] = {
        name = message.nearbyName5,
        id = message.nearbyId5,
        inRace = message.nearbyInRace5,
        elo = message.nearbyElo5,
      }
end)

-- Set the variables
initializationEvent({elo = elo, racesCompleted = racesCompleted})

-- Utility functions

function HsvToRgb(h, s, v)
    local c = v * s
    local x = c * (1 - math.abs((h / 60) % 2 - 1))
    local m = v - c
    local r, g, b

    if h < 60 then r, g, b = c, x, 0
    elseif h < 120 then r, g, b = x, c, 0
    elseif h < 180 then r, g, b = 0, c, x
    elseif h < 240 then r, g, b = 0, x, c
    elseif h < 300 then r, g, b = x, 0, c
    else r, g, b = c, 0, x
    end

    return r + m, g + m, b + m
end

function DrawKey(key, pos, scale)
    ui.transparentWindow("tutorialWindow", pos, scaling.vec2(110,110), function ()

        scale = scale or 1.0
        local imageSize = scaling.vec2(110, 110) * scale

        ui.drawImage(keyPath, vec2(0,0), imageSize)

        ui.pushDWriteFont(fontBold)
        local fontSize = scaling.size(40 * scale)
        local keySize = ui.measureDWriteText(key, fontSize)

        -- Calculate the top-left corner to center the text
        local centeredPos = vec2(
            (imageSize.x - keySize.x) / 2,
            (imageSize.y - keySize.y) / 2
        )

        ui.dwriteDrawText(key, fontSize, centeredPos)
        ui.popDWriteFont()
    end)
end

function GetAvatarPath(steamId)
    local id = tostring(steamId):gsub("%D+$", "")
    return avatarsPath .. id .. ".jpg"
end

function DrawAvatar(player_id, cardPos)
    if loadSteamAvatars then
        local avatarPath = GetAvatarPath(player_id)
        local avatarPoint1 = vec2(cardPos.x + scaling.size(41), cardPos.y + scaling.size(34))
        ui.drawImage(avatarPath, avatarPoint1, avatarPoint1 + scaling.size(106))
    end
end

function DrawText(content, textFont, fontSize, pos)
    ui.pushDWriteFont(textFont)
    ui.dwriteDrawText(content, scaling.size(fontSize), pos)
    ui.popDWriteFont()
end

function FindFontSize(text, startSize, font, maxWidth)
    ui.pushDWriteFont(font)
    local fontSize = scaling.size(startSize)
    local textSize = ui.measureDWriteText(text, fontSize)
    while textSize.x > scaling.size(maxWidth) do
        fontSize = fontSize - scaling.size(2)
        textSize = ui.measureDWriteText(text, fontSize)
    end
    ui.popDWriteFont()
    return fontSize
end

local function checkTimeout(activatedAt, duration)
    if activatedAt ~= nil and os.clock() - activatedAt >= duration then
        return true
    end
    return false
end

function script.drawUI(dt)

    -- Get updated window dimensions each frame
    windowWidth = sim.windowWidth
    windowHeight = sim.windowHeight

    -- Draw standings hud
    if currentHudState ~= HudStates.Off then
        ui.transparentWindow("standingsWindow", vec2(scaling.size(50), windowHeight/2), standingWindowSize, function()
            ui.drawImage(standingsHudPath, vec2(0,0), scaling.vec2(387,213))
            if currentHudState == HudStates.FirstTwo or currentHudState == HudStates.CatAndMouse then
                DrawText("Standings", fontSemiBold, 48, scaling.vec2(44, 37))

                local dots = 2
                if currentHudState == HudStates.CatAndMouse then dots = 3 end

                for i = 1, dots do
                    local result = standings[i]
                    -- Calculate position for each circle (horizontally centered)
                    local circleRadius = scaling.size(25)
                    local spacing = scaling.size(40)
                    local totalWidth = (dots * 2 * circleRadius) + (dots - 1) * spacing
                    local xStart = (standingWindowSize.x - totalWidth) / 2 + circleRadius
                    local xPos = xStart + (2 * circleRadius + spacing) * (i - 1)
                    -- Set color based on result
                    local color
                    if result == RaceResults.Tbd then
                        color = rgbm(0.5, 0.5, 0.5, 0.1) -- Gray for not played
                    elseif result == RaceResults.Win then
                        color = rgbm(0.561, 0.651, 0.235, 1) -- Green for won
                    elseif result == RaceResults.Loss then
                        color = rgbm(0.349, 0.0078, 0.0078, 1) -- Red for lost
                    else
                        color = rgbm(0.5, 0.5, 0.5, 0.7) -- Dark grey for tie
                    end
                    -- Draw circle with appropriate color
                    ui.drawCircleFilled(vec2(xPos, scaling.size(145)), circleRadius, color, 32)
                end
            elseif currentHudState == HudStates.SuddenDeath then
                DrawText("Sudden Death!", fontBold, 32, scaling.vec2(44, 80))
                DrawText("First player to win a round.", font, 18, scaling.vec2(44, 120))
            elseif currentHudState == HudStates.Finished then -- This needs revisiting for different rulesets.
                if standings[3] == RaceResults.Win then
                    DrawText("You win!", fontBold, 32, scaling.vec2(44, 90))
                else
                    DrawText("You lose.", fontBold, 32, scaling.vec2(44, 90))
                end
            end
        end)
    end

    -- Draw elo hud element
    if elo ~= -1 and showElo then
        ui.transparentWindow("eloWindow", scaling.vec2(50, 50), scaling.vec2(196,82), function ()
            local r, g, b = HsvToRgb(hue, 0.7, 0.8)
            ui.drawImage(eloHudPath, scaling.vec2(0, 0), eloHudSize, rgbm(r,g,b,1))
            DrawText("Elo", font, 24, scaling.vec2(11, 31))
            
            -- Draw elo number
            local displayElo = math.floor(elo + 0.5)
            FindEloPos(displayElo)
            DrawText(tostring(displayElo), fontBold, 34, eloNumPos)
        end)
    end

    -- Draw tutorial hud element
    if not hasTutorialHidden then
        if not isTutorialAutoHidden or (car ~= nil and car.speedKmh < 10) then
            ui.transparentWindow("tutorialWindow", vec2(scaling.size(50), windowHeight - scaling.size(465)), scaling.vec2(584, 415), function ()
                ui.drawImage(tutorialPath, vec2(0,0), scaling.vec2(584, 415))
                DrawText("How to play", fontSemiBold, 24, scaling.vec2(32, 32))
            
                local tutorialText = "Chase car overtakes before finish: 1 point to the chase car.\nChase car stays close: draw, no points.\nLead car outruns: 1 point to the lead car.\n\nIf score is tied after the first two rounds: Sudden death."
                DrawText(tutorialText, font, 14, scaling.vec2(32, 78))
                DrawText("Controls", fontSemiBold, 24, scaling.vec2(32, 177))

                local scale = 0.8
                local startX = scaling.size(112)
                local spacingX = scaling.size(120)  -- Space between each key+label pair
                local baseY = windowHeight - scaling.size(250)     -- Fixed vertical position
                local startXText = scaling.size(104)

                for i, binding in ipairs(keyBindings) do
                    local xOffset = startX + (i - 1) * spacingX
                    local XTextOffest = startXText + (i - 1)
                    local keyPos = vec2(xOffset, baseY)
                    local textPos = vec2(XTextOffest - scaling.size(96), scaling.size(96))

                    -- Draw the key and description
                    DrawKey(binding.key, keyPos, scale)
                    DrawText(binding.description, fontSemiBold, 18, textPos)
                end
            end)
        end
    end

    -- Draw invite menu hud.
    if hasInviteMenuOpen then
        ui.transparentWindow("inviteWindow", vec2(windowWidth - scaling.size(818), scaling.size(50)), scaling.vec2(768,1145), function ()
            ui.drawImage(inviteMenuPath, vec2(0,0), scaling.vec2(768,1145))
            local index = 1

            local cardSpacingY = 180  -- Space between cards vertically
            local baseY = 150         -- Starting Y position

            local mousePos = ui.mouseLocalPos()

            if nearbyPlayers[1] ~= nil and nearbyPlayers[1].name == "" then
                DrawText("No other players online", font, 48, scaling.vec2(40, 40))
            end

            while index <= 5 and nearbyPlayers[index] and nearbyPlayers[index].name ~= "" do
                local yOffset = baseY + (index - 1) * cardSpacingY  -- Calculate Y offset

                -- Draw the nearby section title once
                if index == 1 then
                    DrawText("Nearby", font, 48, scaling.vec2(40, 40))
                end

                -- Draw toggle
                if isOutrunAllowed then
                    
                    --Flip depending on if the toggle is on course or not
                    if isToggleCourse then
                        ui.drawImage(togglePath, toggleP1, toggleP2)
                    else
                        ui.drawImage(togglePath, toggleP2, toggleP1)
                    end
                    
                    if not mouseClickHandled and ui.mouseClicked() then
                        if mousePos.x >= toggleP1.x and mousePos.x <= toggleP2.x and
                        mousePos.y >= toggleP1.y and mousePos.y <= toggleP2.y then
                            -- Toggle was clicked
                            isToggleCourse = not isToggleCourse
                            mouseClickHandled = true
                        end
                    end

                    -- Draw toggle text
                    DrawText("Course", fontSemiBold, scaling.size(36), scaling.vec2(334, 50))
                    DrawText("Outrun", fontSemiBold, scaling.size(36), scaling.vec2(545, 50))

                end

                local cardPos = scaling.vec2(32, yOffset)
                local cardSize = scaling.vec2(737, 172)
                local cardBottomRight = cardPos + cardSize

                -- Draw player card
                local cardSize = scaling.vec2(737, yOffset + 172)
                ui.drawImage(playerCardPath, cardPos, cardSize)

                -- Check for mouse click inside card bounds
                if not mouseClickHandled and ui.mouseClicked() then
                    if mousePos.x >= cardPos.x and mousePos.x <= cardBottomRight.x and
                    mousePos.y >= cardPos.y and mousePos.y <= cardBottomRight.y then
                        -- Player card was clicked
                        hasInviteMenuOpen = false;
                        if #courseNames > 1 then
                            -- Show a menu to choose from the different coures.
                            hasCourseSelectOpen = true
                            selectedPlayerId = nearbyPlayers[index].id
                            mouseClickHandled = true
                        else
                            inviteEvent({inviteSenderName = "", inviteRecipientGuid = nearbyPlayers[index].id, isCourse = isToggleCourse})
                        end
                    end
                end

                -- Draw player image
                DrawAvatar(nearbyPlayers[index].id, cardPos)

                -- Draw player name
                ui.pushDWriteFont(fontBold)
                local color = nearbyPlayers[index].inRace and rgbm(0.5, 0.5, 0.5, 1) or rgbm(1, 1, 1, 1)
                
                local fontSize = FindFontSize(nearbyPlayers[index].name, 48, fontBold, 550)
                ui.dwriteDrawTextClipped(nearbyPlayers[index].name, scaling.size(fontSize), cardPos + scaling.vec2(180, 40), cardSize, ui.Alignment.Start, ui.Alignment.Start, false, color)
                ui.popDWriteFont()

                -- Draw elo element
                local eloPoint1 = vec2(cardPos.x + scaling.size(180), cardPos.y + scaling.size(95))
                local eloPoint2 = vec2(eloPoint1.x + eloHudSize.x/2, eloPoint1.y + eloHudSize.y/2)
                local playerElo = nearbyPlayers[index].elo

                local eloColor = GetEloColor(playerElo)
                ui.drawImage(eloHudPath, eloPoint1, eloPoint2, eloColor)

                local eloTextXPos = eloPoint1.x + scaling.size(32)
                if playerElo < 1000 then 
                    eloTextXPos = eloTextXPos + scaling.size(6)
                end

                DrawText(playerElo, fontBold, 16, vec2(eloTextXPos, eloPoint1.y + scaling.size(12)))

                index = index + 1
            end
        end)
    end

    -- Draw course select screen
    if hasCourseSelectOpen then
        ui.transparentWindow("courseSelectWindow", vec2(windowWidth - scaling.size(818), scaling.size(50)), scaling.vec2(768,1145), function ()
            ui.drawImage(inviteMenuPath, vec2(0,0), scaling.vec2(768,1145))

            local cardSpacingY = 180  -- Space between cards vertically
            local baseY = 150         -- Starting Y position

            local mousePos = ui.mouseLocalPos()

            DrawText("Select a course", font, 48, scaling.vec2(40, 40))

            for index, course in ipairs(courseNames) do
                local yOffset = baseY + (index - 1) * cardSpacingY  -- Calculate Y offset

                local cardPos = scaling.vec2(32, yOffset)
                local cardSize = scaling.vec2(737, 172)
                local cardBottomRight = cardPos + cardSize

                -- Draw player card
                local cardSize = scaling.vec2(737, yOffset + 172)
                ui.drawImage(playerCardPath, cardPos, cardSize)

                -- Check for mouse click inside card bounds
                if not mouseClickHandled and ui.mouseClicked() then
                    if mousePos.x >= cardPos.x and mousePos.x <= cardBottomRight.x and
                    mousePos.y >= cardPos.y and mousePos.y <= cardBottomRight.y then
                        -- Send invite with the selected course
                        print(course)
                        inviteEvent({inviteSenderName = "", inviteRecipientGuid = selectedPlayerId, courseName = course, isCourse = isToggleCourse})
                        hasCourseSelectOpen = false
                        mouseClickHandled = true
                    end
                end

                -- Draw course name text.
                ui.pushDWriteFont(fontBold)
                local fontSize = FindFontSize(course, 48, fontBold, 550)
                -- Find the right font size.
                local fontSize = 48 -- Largest possible size
                local textSize = ui.measureDWriteText(course, scaling.size(fontSize))
                while textSize.x > scaling.size(550) do
                    fontSize = fontSize - 8
                    textSize = ui.measureDWriteText(course, scaling.size(fontSize))
                end
                ui.dwriteDrawTextClipped(course, scaling.size(fontSize), cardPos + scaling.vec2(180, 40), cardSize, ui.Alignment.Start, ui.Alignment.Start, false)
                ui.popDWriteFont()
            end

        end)
    end

    -- Draw incoming invite hud element
    if hasActiveInvite == true then
        ui.transparentWindow("receivedInviteWindow", vec2(windowWidth-scaling.size(755), windowHeight-scaling.size(222)), scaling.vec2(705,172), function ()
            ui.drawImage(playerCardPath, vec2(0,0), scaling.vec2(705,172))
            DrawAvatar(inviteSenderId, vec2(0,0))

            -- Draw elo of invite sender.
            local eloColor = GetEloColor(inviteSenderElo)
            local eloPoint1 = scaling.vec2(182, 40)
            local eloPoint2 = vec2(eloPoint1.x + eloHudSize.x/2, eloPoint1.y + eloHudSize.y/2)
            ui.drawImage(eloHudPath, eloPoint1, eloPoint2, eloColor)

            local eloTextXPos = eloPoint1.x + scaling.size(32)
            if inviteSenderElo < 1000 then
                eloTextXPos = eloTextXPos + scaling.size(6)
            end

            DrawText(tostring(inviteSenderElo), fontBold, 16, vec2(eloTextXPos, eloPoint1.y + scaling.size(12)))

            ui.drawImage(mKeyPath, scaling.vec2(560,32), scaling.vec2(670,142))

            local fontSize = FindFontSize(inviteSenderName, 48, fontBold, 270)
            DrawText(tostring(inviteSenderName), fontBold, fontSize, scaling.vec2(295,35))
            DrawText("Challenged you!", font, 36, scaling.vec2(180,95))
        end)
    end

    -- Draw notification hud element
    if hasIncomingNotification then
        local notificationPos = vec2(windowWidth-scaling.size(755), windowHeight-scaling.size(222))
        if hasActiveInvite then
            -- If there is an active invite, draw it above.
            notificationPos = vec2(windowWidth-scaling.size(755), windowHeight-scaling.size(414))
        end

        ui.transparentWindow("notificationWindow", notificationPos, scaling.vec2(705,172), function ()
            ui.drawImage(playerCardPath, vec2(0,0), scaling.vec2(705,172))
            ui.pushDWriteFont(fontSemiBold)
            ui.dwriteDrawTextClipped(notificationMessage, scaling.size(18), scaling.vec2(170,0), scaling.vec2(650,172), ui.Alignment.Start, ui.Alignment.Center, true)
            ui.popDWriteFont()
        end)
    end

    -- Draw countdown
    if isCountdownHudActive then
        local fontSize = scaling.size(48)
        local textSize = ui.measureDWriteText(countdownHudMessage, fontSize)
        -- Compute top-left corner by subtracting half the text size
        local textPos = vec2(
            (windowWidth - textSize.x) / 2,
            (windowHeight - textSize.y) / 2
        )
        ui.transparentWindow("countdownWindow", textPos, scaling.vec2(400,400), function ()
            DrawText(countdownHudMessage, fontBold, fontSize, vec2(0,0))
        end)
    end

    -- Draw forfeit dialog
    if forfeitStartTime ~= nil and currentHudState ~= HudStates.Off then
        local windowPos = vec2(windowWidth-scaling.size(755), windowHeight-scaling.size(222))
        if hasIncomingNotification then
            windowPos = vec2(windowWidth-scaling.size(755), windowHeight-scaling.size(414))
        end
        ui.transparentWindow("forfeitWindow", windowPos, scaling.vec2(705,172), function ()
            -- Draw the notification
            ui.drawImage(playerCardPath, vec2(0,0), scaling.vec2(705,172))
            DrawText("Hold for 3 seconds to forfeit.", fontSemiBold, 18, scaling.vec2(179,40))
        end)
    end
end

function InputCheck()
    mouseClickHandled = false
    if ui.keyboardButtonPressed(ui.KeyIndex.N, false) and not ui.anyItemFocused() and not ui.anyItemActive() then
        -- Send invite
        inviteEvent({inviteSenderName = "nearby", inviteRecipientGuid = 1})
    end
    if ui.keyboardButtonPressed(ui.KeyIndex.I, false) and not ui.anyItemFocused() and not ui.anyItemActive() then
        if hasCourseSelectOpen then 
            hasCourseSelectOpen = false 
        else
            hasInviteMenuOpen = not hasInviteMenuOpen
            if hasInviteMenuOpen then
                local now = os.clock()
                if now - lastLobbyStatusRequest > lobbyCooldown then
                    lastLobbyStatusRequest = now
                    lobbyStatusEvent()
                end
            end
        end
        
    end
    if ui.keyboardButtonPressed(ui.KeyIndex.M, false) and not ui.anyItemFocused() and not ui.anyItemActive() and hasActiveInvite then
        -- Accept invite
        inviteEvent({inviteSenderName = "a", inviteRecipientGuid = 1})
        hasActiveInvite = false
    end
    if ui.keyboardButtonPressed(ui.KeyIndex.H, false) and not ui.anyItemFocused() and not ui.anyItemActive() then
        hasTutorialHidden = not hasTutorialHidden
    end
    if currentHudState ~= HudStates.Off then
        if ui.keyboardButtonDown(ui.KeyIndex.F) then
            if not forfeitLockout then
                if not forfeitStartTime then
                    forfeitStartTime = os.clock()
                elseif os.clock() - forfeitStartTime >= forfeitHoldDuration then
                    forfeitEvent({forfeit = true})
                    forfeitStartTime = nil -- reset so it doesn't trigger repeatedly
                    forfeitLockout = true  -- prevent repeat forfeits on same hold
                end
            end
        else
            forfeitStartTime = nil -- reset if key is released
            forfeitLockout = false
        end
    end
end

function script.update(dt)
    InputCheck()

    if checkTimeout(inviteActivatedAt, 10) then
        hasActiveInvite = false
        inviteActivatedAt = nil
    end

    if checkTimeout(notificationActivatedAt, 10) then
        hasIncomingNotification = false
        notificationActivatedAt = nil
    end

    if checkTimeout(countdownActivatedAt, 5) then
        isCountdownHudActive = false
        countdownActivatedAt = nil
    end
    if discreteMode and showElo and eloShownAt ~= nil and os.clock() - eloShownAt >= 15 then
        eloShownAt = nil
        showElo = false
    end

    if elo ~= targetElo then
        local direction = targetElo > elo and 1 or -1
        local diff = math.abs(targetElo - elo)
        local step = math.max(1, eloAnimSpeed * dt)

        if diff <= step then
            elo = targetElo
        else
            elo = elo + direction * step
        end
    end

    elo = math.floor(elo + 0.5)
end
