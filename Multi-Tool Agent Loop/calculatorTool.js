import 'dotenv/config';
import Anthropic from '@anthropic-ai/sdk';
const aiModel = 'claude-sonnet-4-6';
const client = new Anthropic({
  apiKey: process.env.ANTHROPIC_API_KEY,
});

const tool = {
  name: "calculator",
  description: "A calculator that can perform basic arithmetic operations such as addition, subtraction, multiplication, and division.",  
  input_schema: {
    type: "object",
    properties: { expression: { type: "string", description: "The arithmetic expression to evaluate, e.g., '2 + 2' or '3 * (4 - 1)'" } },
    required: ["expression"],
  }
}

async function calculatorTool(parameters) {
  const { expression } = parameters;
  const result = eval(expression);
  return result.toString();
}



async function calculatorAgent(userMessage) {

const response = await client.messages.create({
  model: aiModel,
  max_tokens: 1024,
  tools: [tool],
  messages: [
    { role: 'user', content: userMessage },
  ],
});

let toolResult;
 if (response.stop_reason === 'tool_use')
 {
      const toolUseBlock = response.content.find(block => block.type === "tool_use")
      if (toolUseBlock.name === "calculator") {
        toolResult = await calculatorTool(toolUseBlock.input);
      }

      const result = await client.messages.create({
        model: aiModel,
        max_tokens: 1024,
        messages: [
          { role: 'user', content: userMessage },
          { role: 'assistant', content: response.content },
          { role: 'user', content: [{type: "tool_result", tool_use_id: toolUseBlock.id, content: toolResult }] },
        ],
      });

      return result.content;

 }
 else{
      return response.content;
 }
}


var userMessage = "What is the result of 5 * (3 + 2)?";
var result = await calculatorAgent(userMessage);
const textBlock = result.find(block => block.type === 'text');
console.log(textBlock ? textBlock.text : result);