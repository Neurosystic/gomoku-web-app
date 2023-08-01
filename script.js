window.addEventListener("load", function () {
    let isPlaceable = false;
    let isGameOver = false;
    let username, gameId, colour;

    const gameBoard = document.querySelector("#gameBoard");
    const startBtn = document.querySelector("#startBtn");
    const quitBtn = document.querySelector("#quitBtn");
    const messageWindow = document.querySelector("#messageWindow");

    // Creating lines on gameboard 
    // inspiration and underlying algorithm (modified) from: https://juejin.cn/post/7116329458335744037 
    const context = gameBoard.getContext("2d");
    context.strokeStyle = "#2D2D2B";
    drawBoardLines();
    // Setting the winning combination:
    // Begin by setting up the winning combination array
    let winArray = [];
    for (let i = 0; i < 15; i++) {
        winArray[i] = [];
        for (let j = 0; j < 15; j++) {
            winArray[i][j] = [];
        }
    }
    // In the horizontal direction 
    let count = 0;
    for (let i = 0; i < 15; i++) {
        for (let j = 0; j < 11; j++) {
            for (let k = 0; k < 5; k++) {
                winArray[j + k][i][count] = true;
            }
            count++;
        }
    }
    // In the vertical direction
    for (let i = 0; i < 15; i++) {
        for (let j = 0; j < 11; j++) {
            for (let k = 0; k < 5; k++) {
                winArray[i][j + k][count] = true;
            }
            count++;
        }
    }
    // Diagonal lines: 
    // "\" orientation 
    for (let i = 0; i < 11; i++) {
        for (let j = 0; j < 11; j++) {
            for (let k = 0; k < 5; k++) {
                winArray[i + k][j + k][count] = true;
            }
            count++;
        }
    }
    // "/" orientation
    for (let i = 0; i < 11; i++) {
        for (let j = 14; j > 3; j--) {
            for (let k = 0; k < 5; k++) {
                winArray[i + k][j - k][count] = true;
            }
            count++;
        }
    }
    // Determining whether stones already exist on chess board in a two dimensional space
    let board = []; // initating a board array that will store stones when they are placed to prevent stones to be placed on to of already occupied intersections
    for (let i = 0; i < 15; i++) {
        board[i] = [];
        for (let j = 0; j < 15; j++) {
            board[i][j] = 0; // initially this will be zero indicating no stones occupying
        }
    }

    // Record stones placed on the board
    let userWinArray = [];
    let opponentWinArray = [];
    for (let i = 0; i < count; i++) {
        userWinArray[i] = 0;
        opponentWinArray[i] = 0;
    }

    // functionality and interactions handled below：
    startBtn.addEventListener("click", async function () {
        try {
            username = await fetchUsername();
            document.querySelector("#usernameDisplay").innerText = `${username}`;
            startBtn.disabled = true;
            await findPlayer();
        } catch (error) {
            messageWindow.innerText = error;
        }
    });

    quitBtn.addEventListener("click", async function () {
        await terminateGame();
        startBtn.disabled = false;
        quitBtn.disabled = true;
    });

    async function terminateGame() {
        try {
            // if game is terminated by a player - need a way to identify this and this is to send a -1,-1 move to the opponet
            // opponent will detect -1 coordinated on the board which will know that game has been terminated by user
            await sendMove("-1,-1");
            setTimeout(async() => {
                await fetch(`http://127.0.0.1:8088/quit?player=${username}&id=${gameId}`, {
                    method: "GET",
                });
            }, 2000);
            messageWindow.innerText = "Thank you for playing";
            gameBoard.disabled = true;
            isGameOver = true;
            isPlaceable = false;
        } catch (error) {
            messageWindow.innerText = error;
        }
    }

    async function fetchUsername() {
        const response = await fetch("http://127.0.0.1:8088/register", {
            method: "GET"
        });
        const username = await response.text();
        return username;
    }

    async function findPlayer() {
        messageWindow.innerText = "Finding an available player, please wait..."
        // create a timer function which will be called every two seconds - this allows active monitoring of the game status
        // and checks whether a player has been matched every two seconds 
        const id = setInterval(async () => {
            const response = await fetch(`http://127.0.0.1:8088/pairme?player=${username}`);
            const json = await response.json();
            if (json.state === "progressing") {
                clearInterval(id); // progressing indicates that players have been matched and game ready to proceed and hence we can clear this timer
                gameId = json.id;
                quitBtn.disabled = false;
                if (username === json.playerOne) {
                    // player one will get to start the game and it is always the black stone that begins the game 
                    colour = "black";
                    isPlaceable = true;
                    messageWindow.innerText = `You are playing against ${json.playerTwo}! It is your turn, please place a stone`;
                    document.querySelector("#opponentDisplay").innerText = json.playerTwo;
                } else {
                    colour = "white";
                    isPlaceable = false;
                    messageWindow.innerText = `You are playing against ${json.playerOne}! Please wait for your opponent to place a stone`;
                    document.querySelector("#opponentDisplay").innerText = json.playerOne;
                    // if player is waiting for opponent move, then we must actively monitor opponents move
                    pullOpponentMove();
                }
            }
        }, 2000);
    }

    gameBoard.addEventListener("click", async function (event) {
        if (isGameOver || !isPlaceable) {
            return;
        }
        const xcord = event.offsetX;
        const ycord = event.offsetY; // retreive the coordinates for where the click action is initiated 
        const a = Math.floor(xcord / 30);
        const b = Math.floor(ycord / 30);
        if (board[a][b] == 0) {
            placeStone(a, b, colour);
            await sendMove(`${a},${b}`); // send the coordinates over 
            board[a][b] = 1; // record the placed stone on board and to prevent player from overlapping stones on board
            isPlaceable = false;
            // Identifying if user won **************************
            let winCount = 0;
            for (let i = 0; i < count; i++) {
                if (winArray[a][b][i]) {
                    userWinArray[i]++;
                    if (userWinArray[i] == 5) {
                        // Show display on browser which would be more ideal
                        messageWindow.innerText = "Congratulations! You win";
                        gameBoard.disabled = true;
                        isGameOver = true;
                        winCount++;
                        break;
                    }
                }
            }
            // if there is no win then the game continues 
            if (winCount == 0) {
                isGameOver = false;
                pullOpponentMove();
            }
        }
    });

    async function sendMove(move) {
        try {
            const response = await fetch(`http://127.0.0.1:8088/mymove?player=${username}&id=${gameId}&move=${move}`, {
                method: "GET",
            });
            if (response.status == 400) {
                // the in case if one player ha exited the game, we get a 400 http response status hence is an indication that opponent has left the game
                messageWindow.innerText = "Opponent has quit the game, you win!";
                gameBoard.disabled = true;
                isGameOver = true;
                isPlaceable = false;
            }
        } catch (error) {
            messageWindow.innerText = error;
        }
    }

    function pullOpponentMove() {
        // setting a timer function that is called every two seconds to fetch for opponents move 
        const id = setInterval(async () => {
            const opponentMove = await fetchOpponentMove();
            if (opponentMove) { // if opponents move exists 
                clearInterval(id); // then clear the timer function/terminate
                const { a, b } = opponentMove;
                if (a == -1 && b == -1) { // if a=x=-1=b=y then this indicates that opponent has left the game 
                    messageWindow.innerText = "Opponent has quit the game, you win!";
                    gameBoard.disabled = true;
                    isGameOver = true;
                    isPlaceable = false;
                    return;
                }
                // otherwise place opponent stone based on the received a and b values 
                if (colour === "white") {
                    placeStone(a, b, "black");
                } else {
                    placeStone(a, b, "white")
                }
                board[a][b] = 1; // record the placed stone on board and to prevent player from overlapping stones on board
                let opponentWinCount = 0;
                for (let i = 0; i < count; i++) {
                    if (winArray[a][b][i]) {
                        opponentWinArray[i]++;
                        if (opponentWinArray[i] == 5) {
                            // Show display on browser which would be more ideal
                            messageWindow.innerText = "You have lost the game :(";
                            gameBoard.disabled = true;
                            isGameOver = true;
                            isPlaceable = false;
                            opponentWinCount++;
                            break;
                        }
                    }
                }
                if (opponentWinCount == 0) {
                    messageWindow.innerText = `It is your turn, please make a move`;
                    isGameOver = false;
                    isPlaceable = true;
                }
            }
        }, 2000)
    }

    async function fetchOpponentMove() {
        try {
            const response = await fetch(`http://127.0.0.1:8088/theirmove?player=${username}&id=${gameId}`, {
                method: "GET",
            });
            const json = await response.text();
            if (json.length === 0) {
                messageWindow.innerText = "Please wait for opponent to make their move";
                return null;
            } else {
                const [a, b] = json.split(",");
                return { a, b }
            }
        } catch (error) {
            messageWindow.innerText = error;
        }
    }

    // Functions:
    // to draw the store according to the coordinates a=x and b=y with the specified colour
    function placeStone(a, b, colourChoice) {
        context.beginPath();
        context.arc(15 + a * 30, 15 + b * 30, 13, 0, 2 * Math.PI);
        context.closePath;
        context.fillStyle = colourChoice;
        context.fill();
    }

    function drawBoardLines() {
        for (let i = 0; i < 15; i++) {
            // Draw horizontal lines on game board
            context.moveTo(15, 15 + i * 30);
            context.lineTo(435, 15 + i * 30);
            context.stroke();
            // Draw vertical lines on game board
            context.moveTo(15 + i * 30, 15);
            context.lineTo(15 + i * 30, 435);
            context.stroke();
        }
    }


});