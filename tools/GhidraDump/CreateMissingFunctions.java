// Creates functions for code that Ghidra's analysis left outside any function, so the dump lists
// those routines. Two kinds: a call target without a function, and the start of a run of instructions
// that belongs to no function (code reached only through jumps or jump tables). A run starts where the
// previous address holds no instruction, belongs to a function, or ends in a return or an unconditional
// jump. Repeats until a pass creates no new function. Run headless (see README.md).
// Adapted from the Open Settlers project's script of the same name.
//@category OpenArmada

import java.util.Set;
import java.util.TreeSet;

import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Instruction;
import ghidra.program.model.listing.InstructionIterator;
import ghidra.program.model.mem.MemoryBlock;
import ghidra.program.model.symbol.FlowType;

public class CreateMissingFunctions extends GhidraScript {

    private final Set<Address> tried = new TreeSet<>();
    private int created;
    private int failed;

    @Override
    protected void run() throws Exception {
        boolean changed = true;
        while (changed && !monitor.isCancelled()) {
            changed = false;
            for (Address start : candidates()) {
                tried.add(start);
                if (getInstructionAt(start) == null) {
                    disassemble(start);
                }
                if (getFunctionContaining(start) == null && getInstructionAt(start) != null
                    && createFunction(start, null) != null) {
                    created++;
                    changed = true;
                } else {
                    failed++;
                }
            }
        }
        println("CreateMissingFunctions: created " + created + ", failed " + failed);
    }

    private Set<Address> candidates() {
        Set<Address> result = new TreeSet<>();
        Instruction previous = null;
        InstructionIterator it = currentProgram.getListing().getInstructions(true);
        while (it.hasNext()) {
            Instruction instruction = it.next();
            Address at = instruction.getAddress();
            MemoryBlock block = currentProgram.getMemory().getBlock(at);
            if (block != null && block.isExecute()) {
                if (instruction.getFlowType().isCall()) {
                    for (Address target : instruction.getFlows()) {
                        if (getFunctionAt(target) == null && !tried.contains(target)) {
                            result.add(target);
                        }
                    }
                }
                if (getFunctionContaining(at) == null && !tried.contains(at) && startsRun(previous, at)) {
                    result.add(at);
                }
            }
            previous = instruction;
        }
        return result;
    }

    private boolean startsRun(Instruction previous, Address at) {
        if (previous == null || !previous.getMaxAddress().next().equals(at)) {
            return true;
        }
        if (getFunctionContaining(previous.getAddress()) != null) {
            return true;
        }
        FlowType flow = previous.getFlowType();
        return flow.isTerminal() || (flow.isJump() && flow.isUnConditional());
    }
}
